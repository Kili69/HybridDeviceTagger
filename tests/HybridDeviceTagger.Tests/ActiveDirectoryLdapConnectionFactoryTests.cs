using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HybridDeviceTagger.Services;

namespace HybridDeviceTagger.Tests;

public sealed class ActiveDirectoryLdapConnectionFactoryTests
{
    [Theory]
    [InlineData(0, false, true)]
    [InlineData(0, true, false)]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    public void UsesNovellClient_ForLinuxTlsTransports(
        int transportValue,
        bool isWindows,
        bool expected)
    {
        var transport = (ActiveDirectoryLdapTransport)transportValue;
        Assert.Equal(
            expected,
            ActiveDirectoryLdapConnectionFactory.UsesNovellClient(transport, isWindows));
    }

    [Fact]
    public void ValidateServerCertificate_AcceptsTrustedSelfSignedCertificate()
    {
        using var certificate = CreateSelfSignedServerCertificate("dc01.example.com");

        var valid = ActiveDirectoryLdapConnectionFactory.ValidateServerCertificate(
            "dc01.example.com",
            certificate,
            [certificate]);

        Assert.True(valid);
    }

    [Fact]
    public void ValidateServerCertificate_RejectsWrongDnsName()
    {
        using var certificate = CreateSelfSignedServerCertificate("dc01.example.com");

        var valid = ActiveDirectoryLdapConnectionFactory.ValidateServerCertificate(
            "dc02.example.com",
            certificate,
            [certificate]);

        Assert.False(valid);
    }

    [Fact]
    public void ValidateServerCertificate_RejectsUntrustedIssuer()
    {
        using var certificate = CreateSelfSignedServerCertificate("dc01.example.com");
        using var otherCertificate = CreateSelfSignedServerCertificate("dc01.example.com");

        var valid = ActiveDirectoryLdapConnectionFactory.ValidateServerCertificate(
            "dc01.example.com",
            certificate,
            [otherCertificate]);

        Assert.False(valid);
    }

    [Fact]
    public void ValidateServerCertificate_AcceptsMatchingRootAmongMultipleCertificates()
    {
        using var certificate = CreateSelfSignedServerCertificate("dc01.example.com");
        using var otherCertificate = CreateSelfSignedServerCertificate("dc02.example.com");

        var valid = ActiveDirectoryLdapConnectionFactory.ValidateServerCertificate(
            "dc01.example.com",
            certificate,
            [otherCertificate, certificate]);

        Assert.True(valid);
    }

    [Fact]
    public void ValidateServerCertificate_RejectsCaIssuedCertificateWithoutRevocationInformation()
    {
        using var certificates = CreateCertificateChain("dc01.example.com");

        var valid = ActiveDirectoryLdapConnectionFactory.ValidateServerCertificate(
            "dc01.example.com",
            certificates.Server,
            [certificates.Root]);

        Assert.False(valid);
    }

    private static X509Certificate2 CreateSelfSignedServerCertificate(string dnsName)
    {
        using var key = RSA.Create(2048);
        var request = CreateServerCertificateRequest(dnsName, key);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddHours(12));
        return new X509Certificate2(certificate.Export(X509ContentType.Cert));
    }

    [Fact]
    public void LoadTrustedCertificates_ReturnsEmptyWithoutConfiguredPath()
    {
        Assert.Empty(ActiveDirectoryLdapConnectionFactory.LoadTrustedCertificates(null));
    }

    [Fact]
    public void LoadTrustedCertificates_LoadsPemAndDerFiles()
    {
        using var certificates = CreateCertificateChain("dc01.example.com");
        var pemPath = Path.GetTempFileName();
        var derPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(
                pemPath,
                certificates.Root.ExportCertificatePem() + Environment.NewLine +
                certificates.Server.ExportCertificatePem());
            File.WriteAllBytes(derPath, certificates.Root.Export(X509ContentType.Cert));

            var pem = ActiveDirectoryLdapConnectionFactory.LoadTrustedCertificates(pemPath);
            var der = ActiveDirectoryLdapConnectionFactory.LoadTrustedCertificates(derPath);

            Assert.Equal(
                [certificates.Root.Thumbprint, certificates.Server.Thumbprint],
                pem.Select(certificate => certificate.Thumbprint));
            Assert.Equal(certificates.Root.Thumbprint, Assert.Single(der).Thumbprint);
        }
        finally
        {
            File.Delete(pemPath);
            File.Delete(derPath);
        }
    }

    private static CertificateChain CreateCertificateChain(string dnsName)
    {
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest(
            "CN=HybridDeviceTagger Test Root",
            rootKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                true));
        using var rootWithKey = rootRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddDays(1));
        var root = new X509Certificate2(rootWithKey.Export(X509ContentType.Cert));

        using var serverKey = RSA.Create(2048);
        var serverRequest = CreateServerCertificateRequest(dnsName, serverKey);

        var serialNumber = RandomNumberGenerator.GetBytes(16);
        using var serverCertificate = serverRequest.Create(
            rootWithKey,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddHours(12),
            serialNumber);
        var server = new X509Certificate2(serverCertificate.Export(X509ContentType.Cert));
        return new CertificateChain(root, server);
    }

    private static CertificateRequest CreateServerCertificateRequest(string dnsName, RSA key)
    {
        var request = new CertificateRequest(
            $"CN={dnsName}",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                true));
        var enhancedKeyUsage = new OidCollection
        {
            new("1.3.6.1.5.5.7.3.1")
        };
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(enhancedKeyUsage, true));
        var subjectAlternativeName = new SubjectAlternativeNameBuilder();
        subjectAlternativeName.AddDnsName(dnsName);
        request.CertificateExtensions.Add(subjectAlternativeName.Build());
        return request;
    }

    private sealed record CertificateChain(
        X509Certificate2 Root,
        X509Certificate2 Server) : IDisposable
    {
        public void Dispose()
        {
            Root.Dispose();
            Server.Dispose();
        }
    }
}