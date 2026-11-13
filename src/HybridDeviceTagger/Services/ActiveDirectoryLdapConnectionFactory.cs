/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using System.DirectoryServices.Protocols;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Services;

/// <summary>Creates authenticated LDAP v3 connections using the configured LDAPS endpoint.</summary>
public sealed class ActiveDirectoryLdapConnectionFactory
{
    /// <summary>The validated LDAP endpoint, port, and fallback settings.</summary>
    private readonly ActiveDirectoryOptions _options;

    /// <summary>
    /// The optional private trust anchors loaded once at construction; empty when the platform trust
    /// store is used.
    /// </summary>
    private readonly IReadOnlyList<X509Certificate2> _trustedCertificates;

    /// <summary>The structured logger; it never receives credentials.</summary>
    private readonly ILogger<ActiveDirectoryLdapConnectionFactory> _logger;

    /// <summary>Initializes a new instance of the <see cref="ActiveDirectoryLdapConnectionFactory"/> class.</summary>
    /// <param name="options">The validated Active Directory connection settings.</param>
    /// <param name="logger">The structured logger.</param>
    /// <exception cref="IOException">The configured trusted certificate file cannot be read.</exception>
    /// <exception cref="CryptographicException">
    /// The configured trusted certificate file does not contain a valid certificate.
    /// </exception>
    public ActiveDirectoryLdapConnectionFactory(
        IOptions<ActiveDirectoryOptions> options,
        ILogger<ActiveDirectoryLdapConnectionFactory> logger)
    {
        _options = options.Value;
        _trustedCertificates = LoadTrustedCertificates(_options.TrustedCertificatePath);
        _logger = logger;
    }

    /// <summary>Creates and binds a connection, disposing it if the bind fails.</summary>
    /// <param name="credential">The directory credential used for the bind.</param>
    /// <param name="server">The optional domain DNS name; the configured server is used when omitted.</param>
    /// <returns>An authenticated connection owned by the caller.</returns>
    /// <remarks>
    /// LDAPS is always attempted first. Only when <see cref="ActiveDirectoryOptions.AllowLdapFallback"/>
    /// is enabled and LDAPS is unreachable does the factory retry with signed and sealed Negotiate
    /// authentication on the fallback port. Password changes must use
    /// <see cref="CreateSecureBoundConnection"/> instead.
    /// </remarks>
    /// <exception cref="LdapException">The connection or bind failed on every permitted transport.</exception>
    public LdapConnection CreateBoundConnection(
        StoredDirectoryCredential credential,
        string? server = null)
    {
        var targetServer = server ?? _options.Server;
        _logger.LogInformation(
            "Connecting to Active Directory server {Server} on LDAPS port {Port}",
            targetServer,
            _options.Port);
        try
        {
            return CreateBoundConnection(credential, targetServer, _options.Port, useSsl: true);
        }
        catch (LdapException exception) when (
            _options.AllowLdapFallback && IsConnectionUnavailable(exception))
        {
            _logger.LogWarning(
                exception,
                "LDAPS connection to {Server}:{LdapsPort} failed. Falling back to signed and sealed LDAP Negotiate authentication on port {LdapPort}",
                targetServer,
                _options.Port,
                _options.LdapFallbackPort);
            try
            {
                return CreateBoundConnection(
                    credential,
                    targetServer,
                    _options.LdapFallbackPort,
                    useSsl: false);
            }
            catch (LdapException fallbackException)
            {
                _logger.LogError(
                    fallbackException,
                    "LDAP fallback connection to {Server}:{Port} failed",
                    targetServer,
                    _options.LdapFallbackPort);
                throw;
            }
        }
    }

    /// <summary>Creates an LDAPS connection without allowing an LDAP fallback.</summary>
    /// <param name="credential">The directory credential used for the bind.</param>
    /// <returns>An authenticated secure connection owned by the caller.</returns>
    /// <remarks>Password changes require this method because Active Directory only accepts them over TLS.</remarks>
    /// <exception cref="LdapException">The LDAPS connection or bind failed.</exception>
    public LdapConnection CreateSecureBoundConnection(StoredDirectoryCredential credential) =>
        CreateBoundConnection(credential, _options.Server, _options.Port, useSsl: true);

    /// <summary>Creates and binds one LDAP v3 connection using the selected transport mode.</summary>
    /// <param name="credential">The directory credential used for authentication.</param>
    /// <param name="server">The DNS name of the target domain controller or domain.</param>
    /// <param name="port">The LDAPS or LDAP endpoint port.</param>
    /// <param name="useSsl">
    /// Selects Basic authentication inside TLS when <see langword="true"/> or signed and sealed
    /// Negotiate authentication when <see langword="false"/>.
    /// </param>
    /// <returns>A bound connection that must be disposed by the caller.</returns>
    /// <exception cref="LdapException">The connection or bind failed; the connection is disposed.</exception>
    private LdapConnection CreateBoundConnection(
        StoredDirectoryCredential credential,
        string server,
        int port,
        bool useSsl)
    {
        var identifier = new LdapDirectoryIdentifier(server, port);
        var networkCredential = new NetworkCredential(credential.Username, credential.Password);
        var authenticationType = useSsl ? AuthType.Basic : AuthType.Negotiate;
        var connection = new LdapConnection(identifier, networkCredential, authenticationType)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.SecureSocketLayer = useSsl;
        // On Linux, OpenLDAP validates against LDAPTLS_CACERT, which the deployment points at the
        // packaged trust anchor. Windows needs explicit validation for a private trust anchor.
        if (useSsl && OperatingSystem.IsWindows() && _trustedCertificates.Count > 0)
        {
            connection.SessionOptions.VerifyServerCertificate = (_, certificate) =>
            ValidateServerCertificate(server, certificate, _trustedCertificates);
        }
        if (!useSsl)
        {
            connection.SessionOptions.Signing = true;
            connection.SessionOptions.Sealing = true;
        }

        try
        {
            connection.Bind();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>Identifies connectivity errors for which an authenticated LDAP fallback is safe.</summary>
    /// <remarks>
    /// Credential and authorization failures are deliberately excluded so invalid credentials do
    /// not weaken the transport selected by the caller. The handled codes are 51 (busy),
    /// 52 (unavailable), 81 (server down), 85 (timeout), and 91 (connect error).
    /// </remarks>
    /// <param name="exception">The LDAPS failure to classify.</param>
    /// <returns><see langword="true"/> when the failure indicates the endpoint was unreachable.</returns>
    private static bool IsConnectionUnavailable(LdapException exception) =>
        exception.ErrorCode is 51 or 52 or 81 or 85 or 91;

    /// <summary>Validates the LDAP server certificate against a configured private trust anchor.</summary>
    /// <remarks>
    /// The chain must terminate in one of the configured anchors (the machine store is ignored), the
    /// certificate must allow server authentication, and its DNS name must match the target server.
    /// Revocation is not checked because private domain PKIs are often unreachable from Azure.
    /// </remarks>
    /// <param name="server">The DNS name used to connect.</param>
    /// <param name="certificate">The certificate presented by the server.</param>
    /// <param name="trustedCertificates">The private trust anchors.</param>
    /// <returns><see langword="true"/> when the chain and DNS identity are valid.</returns>
    internal static bool ValidateServerCertificate(
        string server,
        X509Certificate certificate,
        IReadOnlyCollection<X509Certificate2> trustedCertificates)
    {
        using var serverCertificate = new X509Certificate2(
            certificate.Export(X509ContentType.Cert));
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(trustedCertificates.ToArray());
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));

        return chain.Build(serverCertificate) && MatchesDnsName(serverCertificate, server);
    }

    /// <summary>Loads the optional packaged DER or PEM trust anchors.</summary>
    /// <remarks>
    /// Trust anchors are immutable deployment content. They cannot be changed at runtime, so a
    /// principal with storage or API access cannot redirect the LDAPS bind to another server.
    /// Relative paths resolve against the application base directory. Certificates with private keys
    /// are ignored when reading PEM content.
    /// </remarks>
    /// <param name="path">The optional absolute or application-relative certificate path.</param>
    /// <returns>The trust anchors, or an empty list when no path is configured.</returns>
    /// <exception cref="CryptographicException">The PEM file contains no certificate.</exception>
    internal static IReadOnlyList<X509Certificate2> LoadTrustedCertificates(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        var fullPath = Path.IsPathRooted(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, path);
        var bytes = File.ReadAllBytes(fullPath);
        var text = Encoding.ASCII.GetString(bytes);
        if (!text.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal))
        {
            return [new X509Certificate2(bytes)];
        }

        var certificates = new X509Certificate2Collection();
        certificates.ImportFromPem(text);
        if (certificates.Count == 0)
        {
            throw new CryptographicException(
                $"The trusted certificate file '{path}' does not contain a certificate.");
        }

        return [.. certificates.Where(certificate => !certificate.HasPrivateKey)];
    }

    /// <summary>Checks the certificate DNS identity, including a single-label wildcard.</summary>
    /// <param name="certificate">The server certificate.</param>
    /// <param name="server">The DNS name used to connect.</param>
    /// <returns>
    /// <see langword="true"/> when the names match case-insensitively or a leading <c>*.</c> wildcard
    /// covers exactly one label of <paramref name="server"/>.
    /// </returns>
    internal static bool MatchesDnsName(X509Certificate2 certificate, string server)
    {
        var certificateName = certificate.GetNameInfo(X509NameType.DnsName, forIssuer: false);
        if (string.Equals(certificateName, server, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!certificateName.StartsWith("*.", StringComparison.Ordinal))
        {
            return false;
        }

        var serverLabels = server.Split('.');
        var certificateLabels = certificateName.Split('.');
        return serverLabels.Length == certificateLabels.Length &&
            string.Equals(
                string.Join('.', serverLabels.Skip(1)),
                string.Join('.', certificateLabels.Skip(1)),
                StringComparison.OrdinalIgnoreCase);
    }
}