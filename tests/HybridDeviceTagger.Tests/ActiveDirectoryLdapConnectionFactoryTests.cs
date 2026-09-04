using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HybridDeviceTagger.Services;

namespace HybridDeviceTagger.Tests;

public sealed class ActiveDirectoryLdapConnectionFactoryTests
{
    [Fact]
    public void ValidateServerCertificate_AcceptsCertificateFromConfiguredRoot()
    {
        using var certificates = CreateCertificateChain("dc01.example.com");

        var valid = ActiveDirectoryLdapConnectionFactory.ValidateServerCertificate(
            "dc01.example.com",
            certificates.Server,
            certificates.Root);

        Assert.True(valid);
    }

    [Fact]
    public void ValidateServerCertificate_RejectsWrongDnsName()
    {
        using var certificates = CreateCertificateChain("dc01.example.com");

        var valid = ActiveDirectoryLdapConnectionFactory.ValidateServerCertificate(
            "dc02.example.com",
            certificates.Server,
            certificates.Root);

        Assert.False(valid);
    }

    [Fact]
    public void ValidateServerCertificate_RejectsUntrustedIssuer()
    {
        using var certificates = CreateCertificateChain("dc01.example.com");
        using var otherCertificates = CreateCertificateChain("dc01.example.com");

        var valid = ActiveDirectoryLdapConnectionFactory.ValidateServerCertificate(
            "dc01.example.com",
            certificates.Server,
            otherCertificates.Root);

        Assert.False(valid);
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
        var serverRequest = new CertificateRequest(
            $"CN={dnsName}",
            serverKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        serverRequest.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));
        serverRequest.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                true));
        var enhancedKeyUsage = new OidCollection
        {
            new("1.3.6.1.5.5.7.3.1")
        };
        serverRequest.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(enhancedKeyUsage, true));
        var subjectAlternativeName = new SubjectAlternativeNameBuilder();
        subjectAlternativeName.AddDnsName(dnsName);
        serverRequest.CertificateExtensions.Add(subjectAlternativeName.Build());

        var serialNumber = RandomNumberGenerator.GetBytes(16);
        using var serverCertificate = serverRequest.Create(
            rootWithKey,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddHours(12),
            serialNumber);
        var server = new X509Certificate2(serverCertificate.Export(X509ContentType.Cert));
        return new CertificateChain(root, server);
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