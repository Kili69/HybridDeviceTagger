using System.ComponentModel.DataAnnotations;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using HybridDeviceTagger.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HybridDeviceTagger.Tests;

public sealed class ActiveDirectoryConfigurationTests
{
    [Fact]
    public async Task GetAsync_UsesInitialDomainOnlyForMissingBlob()
    {
        var client = new Mock<BlobClient>();
        client.Setup(value => value.DownloadContentAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not found"));
        var store = CreateStore(client);

        var snapshot = await store.GetAsync(CancellationToken.None);

        Assert.Equal("initial.example", snapshot.Domain);
        Assert.Equal("appSetting", snapshot.Source);
        Assert.Null(snapshot.ETag);

        client.Setup(value => value.DownloadContentAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(403, "Forbidden"));
        await Assert.ThrowsAsync<RequestFailedException>(() => store.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_ReturnsPersistedDomainAndVersion()
    {
        var client = new Mock<BlobClient>();
        var details = BlobsModelFactory.BlobDownloadDetails(
            eTag: new ETag("\"version\""), lastModified: DateTimeOffset.UtcNow);
        client.Setup(value => value.DownloadContentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(
                BlobsModelFactory.BlobDownloadResult(
                    BinaryData.FromObjectAsJson(" CORRECTED.EXAMPLE. "), details),
                Mock.Of<Response>()));

        var snapshot = await CreateStore(client).GetAsync(CancellationToken.None);

        Assert.Equal("corrected.example", snapshot.Domain);
        Assert.Equal("\"version\"", snapshot.ETag);
        Assert.Equal("blob", snapshot.Source);
    }

    [Theory]
    [InlineData("")]
    [InlineData("_ldap._tcp.dc._msdcs.contoso.com")]
    [InlineData("https://contoso.com")]
    [InlineData("contoso")]
    public async Task ReplaceAsync_RejectsInvalidDomainBeforeStorageAccess(string domain)
    {
        var client = new Mock<BlobClient>(MockBehavior.Strict);
        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateStore(client).ReplaceAsync(domain, null, CancellationToken.None));
        client.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\"previous\"")]
    public async Task ReplaceAsync_NormalizesDomainAndUsesConditionalWrite(string? expectedETag)
    {
        var client = new Mock<BlobClient>();
        client.Setup(value => value.DownloadContentAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not found"));
        BlobUploadOptions? uploadOptions = null;
        string? storedDomain = null;
        client.Setup(value => value.UploadAsync(
                It.IsAny<BinaryData>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()))
            .Callback<BinaryData, BlobUploadOptions, CancellationToken>((data, options, _) =>
            {
                storedDomain = System.Text.Json.JsonDocument.Parse(data.ToString()).RootElement
                    .GetProperty("domain").GetString();
                uploadOptions = options;
            })
            .ThrowsAsync(new RequestFailedException(412, "Precondition failed"));

        await Assert.ThrowsAsync<RequestFailedException>(() =>
            CreateStore(client).ReplaceAsync(" CORRECTED.EXAMPLE. ", expectedETag, CancellationToken.None));

        Assert.Equal("corrected.example", storedDomain);
        Assert.NotNull(uploadOptions);
        if (expectedETag is null)
        {
            Assert.Equal(ETag.All, uploadOptions.Conditions.IfNoneMatch);
        }
        else
        {
            Assert.Equal(new ETag(expectedETag), uploadOptions.Conditions.IfMatch);
        }
    }

    [Fact]
    public async Task ReplaceAsync_PreservesServiceAccountAndReadsLegacyDomainBlob()
    {
        var client = new Mock<BlobClient>();
        var details = BlobsModelFactory.BlobDownloadDetails(eTag: new ETag("\"v1\""), lastModified: DateTimeOffset.UtcNow);
        client.SetupSequence(value => value.DownloadContentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(
                BlobsModelFactory.BlobDownloadResult(BinaryData.FromObjectAsJson("legacy.example"), details),
                Mock.Of<Response>()))
            .ReturnsAsync(Response.FromValue(
                BlobsModelFactory.BlobDownloadResult(
                    BinaryData.FromString("""{"domain":"legacy.example","bindUsername":"renamed@example.com","bindUserDistinguishedName":""}"""),
                    details),
                Mock.Of<Response>()));
        string? stored = null;
        client.Setup(value => value.UploadAsync(
                It.IsAny<BinaryData>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()))
            .Callback<BinaryData, BlobUploadOptions, CancellationToken>((data, _, _) => stored = data.ToString())
            .ReturnsAsync(Response.FromValue(
                BlobsModelFactory.BlobContentInfo(new ETag("\"v2\""), DateTimeOffset.UtcNow, null, null, null, null, 0),
                Mock.Of<Response>()));
        var store = CreateStore(client);

        var legacy = await store.GetAsync(CancellationToken.None);
        var snapshot = await store.ReplaceAsync("corrected.example", "\"v1\"", CancellationToken.None);

        Assert.Equal("legacy.example", legacy.Domain);
        Assert.Null(legacy.BindUsername);
        Assert.Equal("renamed@example.com", snapshot.BindUsername);
        Assert.Contains("\"bindUsername\":\"renamed@example.com\"", stored, StringComparison.Ordinal);
        Assert.Contains("\"domain\":\"corrected.example\"", stored, StringComparison.Ordinal);
        Assert.Equal(
            ("renamed@example.com", string.Empty),
            snapshot.GetServiceAccount(new ActiveDirectoryOptions { BindUsername = "initial@example.com", BindUserDistinguishedName = "CN=initial" }));
        Assert.Equal(
            ("initial@example.com", "CN=initial"),
            legacy.GetServiceAccount(new ActiveDirectoryOptions { BindUsername = "initial@example.com", BindUserDistinguishedName = "CN=initial" }));
    }

    [Theory]
    [InlineData("not-a-upn", null)]
    [InlineData("svc@example.com", "not a dn")]
    public async Task ReplaceServiceAccountAsync_RejectsMalformedInputBeforeStorageAccess(string username, string? distinguishedName)
    {
        var client = new Mock<BlobClient>(MockBehavior.Strict);
        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateStore(client).ReplaceServiceAccountAsync(username, distinguishedName, null, CancellationToken.None));
        client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ForestConnections_ReadCorrectedDomainWithoutRestart()
    {
        var store = new Mock<IActiveDirectoryConfigurationStore>();
        store.SetupSequence(value => value.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveDirectoryConfigurationSnapshot("wrong.example", null, null, "appSetting"))
            .ReturnsAsync(new ActiveDirectoryConfigurationSnapshot("correct.example", "\"new\"", null, "blob"));
        var domains = new List<string>();
        var resolver = new Mock<IActiveDirectoryServerResolver>();
        resolver.Setup(value => value.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((domain, _) => domains.Add(domain))
            .ThrowsAsync(new InvalidOperationException("Stop before opening LDAP."));
        var factory = new ActiveDirectoryLdapConnectionFactory(
            Microsoft.Extensions.Options.Options.Create(new ActiveDirectoryOptions()),
            resolver.Object,
            NullLogger<ActiveDirectoryLdapConnectionFactory>.Instance,
            store.Object,
            Mock.Of<ITrustedCertificateStore>());
        var credential = new StoredDirectoryCredential("svc@example.com", "password", null);

        for (var index = 0; index < 2; index++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                factory.CreateSecureBoundConnectionAsync(credential, CancellationToken.None));
        }

        Assert.Equal(new[] { "wrong.example", "correct.example" }, domains);
    }

    [Fact]
    public void DirectoryOptions_AllowDeferredAccountDnButStillRequireTls()
    {
        var options = new ActiveDirectoryOptions { Domain = "example.com", BindUsername = "svc@example.com" };
        var results = new List<ValidationResult>();
        Assert.True(Validator.TryValidateObject(options, new ValidationContext(options), results, true));

        options = new ActiveDirectoryOptions
        {
            Domain = "example.com", BindUsername = "svc@example.com", UseSsl = false
        };
        Assert.False(Validator.TryValidateObject(options, new ValidationContext(options), results, true));
    }

    private static BlobActiveDirectoryConfigurationStore CreateStore(Mock<BlobClient> client) =>
        new(client.Object, Microsoft.Extensions.Options.Options.Create(new ActiveDirectoryOptions
        {
            Domain = "initial.example"
        }));
}
