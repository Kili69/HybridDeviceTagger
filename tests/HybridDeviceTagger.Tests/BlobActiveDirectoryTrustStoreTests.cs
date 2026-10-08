using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HybridDeviceTagger.Options;
using HybridDeviceTagger.Services;
using Microsoft.Extensions.Options;
using Moq;

namespace HybridDeviceTagger.Tests;

public sealed class BlobActiveDirectoryTrustStoreTests : IDisposable
{
    private readonly string _fallbackPath;
    private readonly X509Certificate2 _fallbackCertificate;

    public BlobActiveDirectoryTrustStoreTests()
    {
        _fallbackCertificate = CreateCertificate("Fallback Root");
        _fallbackPath = Path.GetTempFileName();
        File.WriteAllBytes(_fallbackPath, _fallbackCertificate.Export(X509ContentType.Cert));
    }

    [Fact]
    public async Task GetSnapshotAsync_WhenBlobDoesNotExist_ReturnsPackageFallback()
    {
        var store = CreateStore(CreateBlobClient().Object);

        var snapshot = await store.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal("package", snapshot.Source);
        Assert.Null(snapshot.ETag);
        var certificate = Assert.Single(snapshot.Certificates);
        Assert.Equal(_fallbackCertificate.Thumbprint, certificate.Thumbprint);
    }

    [Fact]
    public async Task GetSnapshotAsync_WithoutPackagedCertificate_ReturnsEmptySnapshot()
    {
        var store = new BlobActiveDirectoryTrustStore(
            CreateBlobClient().Object,
            Microsoft.Extensions.Options.Options.Create(
                new ActiveDirectoryOptions { TrustedCertificatePath = string.Empty }));

        var snapshot = await store.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal("none", snapshot.Source);
        Assert.Empty(snapshot.Certificates);
        Assert.Empty(store.GetCertificates());
    }

    [Fact]
    public async Task AddAsync_WithoutPackagedCertificate_PersistsFirstCertificate()
    {
        using var addedCertificate = CreateCertificate("Added Root");
        var store = new BlobActiveDirectoryTrustStore(
            CreateBlobClient().Object,
            Microsoft.Extensions.Options.Options.Create(
                new ActiveDirectoryOptions { TrustedCertificatePath = null }));

        var snapshot = await store.AddAsync(
            addedCertificate.Export(X509ContentType.Cert),
            null,
            CancellationToken.None);

        Assert.Equal("blob", snapshot.Source);
        Assert.Equal(addedCertificate.Thumbprint, Assert.Single(snapshot.Certificates).Thumbprint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddAsync_AcceptsDerAndPemAndPersistsBothCertificates(bool usePem)
    {
        using var addedCertificate = CreateCertificate("Added Root");
        var blobClient = CreateBlobClient();
        var store = CreateStore(blobClient.Object);
        var bytes = usePem
            ? System.Text.Encoding.ASCII.GetBytes(addedCertificate.ExportCertificatePem())
            : addedCertificate.Export(X509ContentType.Cert);

        var snapshot = await store.AddAsync(bytes, null, CancellationToken.None);
        var persisted = store.GetCertificates();

        Assert.Equal("blob", snapshot.Source);
        Assert.Equal(2, persisted.Count);
        Assert.Contains(persisted, certificate => certificate.Thumbprint == _fallbackCertificate.Thumbprint);
        Assert.Contains(persisted, certificate => certificate.Thumbprint == addedCertificate.Thumbprint);
        DisposeCertificates(persisted);
    }

    [Fact]
    public async Task AddAsync_WhenCertificateAlreadyExists_IsIdempotent()
    {
        var blobClient = CreateBlobClient();
        var store = CreateStore(blobClient.Object);
        var bytes = _fallbackCertificate.Export(X509ContentType.Cert);

        var snapshot = await store.AddAsync(bytes, null, CancellationToken.None);

        Assert.Equal("package", snapshot.Source);
        Assert.Null(snapshot.ETag);
        blobClient.Verify(client => client.UploadAsync(
            It.IsAny<BinaryData>(),
            It.IsAny<BlobUploadOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveAsync_RemovesPersistedCertificate()
    {
        using var addedCertificate = CreateCertificate("Added Root");
        var store = CreateStore(CreateBlobClient().Object);
        var added = await store.AddAsync(
            addedCertificate.Export(X509ContentType.Cert),
            null,
            CancellationToken.None);

        var removed = await store.RemoveAsync(
            addedCertificate.Thumbprint,
            added.ETag,
            CancellationToken.None);

        Assert.Equal("blob", removed.Source);
        Assert.Equal(_fallbackCertificate.Thumbprint, Assert.Single(removed.Certificates).Thumbprint);
    }

    [Fact]
    public async Task RemoveAsync_WhenRemovingFinalCertificate_RejectsMutation()
    {
        var store = CreateStore(CreateBlobClient().Object);

        var exception = await Assert.ThrowsAsync<TrustedCertificateValidationException>(() =>
            store.RemoveAsync(_fallbackCertificate.Thumbprint, null, CancellationToken.None));

        Assert.Contains("At least one", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mutations_RejectInvalidCertificateAndThumbprint()
    {
        var store = CreateStore(CreateBlobClient().Object);

        await Assert.ThrowsAsync<TrustedCertificateValidationException>(() =>
            store.AddAsync([1, 2, 3], null, CancellationToken.None));
        await Assert.ThrowsAsync<TrustedCertificateValidationException>(() =>
            store.RemoveAsync("not-a-thumbprint", null, CancellationToken.None));
    }

    [Fact]
    public async Task AddAsync_WhenETagIsStale_ThrowsConflict()
    {
        using var addedCertificate = CreateCertificate("Added Root");
        var store = CreateStore(CreateBlobClient(rejectUploads: true).Object);

        await Assert.ThrowsAsync<TrustedCertificateConflictException>(() =>
            store.AddAsync(
                addedCertificate.Export(X509ContentType.Cert),
                "\"stale\"",
                CancellationToken.None));
    }

    public void Dispose()
    {
        _fallbackCertificate.Dispose();
        File.Delete(_fallbackPath);
    }

    private BlobActiveDirectoryTrustStore CreateStore(BlobClient blobClient) =>
        new(blobClient, Microsoft.Extensions.Options.Options.Create(new ActiveDirectoryOptions
        {
            TrustedCertificatePath = _fallbackPath
        }));

    private static Mock<BlobClient> CreateBlobClient(bool rejectUploads = false)
    {
        BinaryData? storedContent = null;
        var etagCounter = 0;
        var client = new Mock<BlobClient>();
        client.Setup(blob => blob.DownloadContent())
            .Returns(() => storedContent is null
                ? throw new RequestFailedException(404, "Not found")
                : Response.FromValue(
                    BlobsModelFactory.BlobDownloadResult(
                        storedContent,
                        BlobsModelFactory.BlobDownloadDetails(
                            lastModified: DateTimeOffset.UtcNow,
                            eTag: new ETag($"\"etag-{etagCounter}\""))),
                    Mock.Of<Response>()));
        client.Setup(blob => blob.DownloadContentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => storedContent is null
                ? throw new RequestFailedException(404, "Not found")
                : Response.FromValue(
                    BlobsModelFactory.BlobDownloadResult(
                        storedContent,
                        BlobsModelFactory.BlobDownloadDetails(
                            lastModified: DateTimeOffset.UtcNow,
                            eTag: new ETag($"\"etag-{etagCounter}\""))),
                    Mock.Of<Response>()));
        client.Setup(blob => blob.UploadAsync(
                It.IsAny<BinaryData>(),
                It.IsAny<BlobUploadOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((BinaryData content, BlobUploadOptions _, CancellationToken _) =>
            {
                if (rejectUploads)
                {
                    throw new RequestFailedException(412, "Condition not met");
                }

                storedContent = content;
                etagCounter++;
                return Response.FromValue(
                    BlobsModelFactory.BlobContentInfo(
                        new ETag($"\"etag-{etagCounter}\""),
                        DateTimeOffset.UtcNow,
                        null,
                        null,
                        0),
                    Mock.Of<Response>());
            });
        return client;
    }

    private static X509Certificate2 CreateCertificate(string commonName)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={commonName}",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    private static void DisposeCertificates(IEnumerable<X509Certificate2> certificates)
    {
        foreach (var certificate in certificates)
        {
            certificate.Dispose();
        }
    }
}