/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * Copyright 2026 Andreas Lucas
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 * SPDX-License-Identifier: Apache-2.0
 */

using System.DirectoryServices.Protocols;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NovellLdapConnection = Novell.Directory.Ldap.LdapConnection;
using NovellLdapConnectionOptions = Novell.Directory.Ldap.LdapConnectionOptions;
using NovellLdapException = Novell.Directory.Ldap.LdapException;

namespace HybridDeviceTagger.Services;

/// <summary>Records one LDAP connection attempt for diagnostics; contains no credentials.</summary>
/// <param name="Server">The domain controller DNS name.</param>
/// <param name="Port">The TCP port.</param>
/// <param name="Transport">LDAPS or StartTLS.</param>
/// <param name="Succeeded">Whether TLS negotiation and bind succeeded.</param>
/// <param name="ResultCode">The LDAP result code of a failure, or null after success.</param>
/// <param name="Error">The failure reason, including certificate validation details.</param>
public sealed record ActiveDirectoryLdapConnectionAttempt(
    string Server,
    int Port,
    string Transport,
    bool Succeeded,
    int? ResultCode,
    string? Error);

/// <summary>Creates authenticated LDAP v3 connections using the configured LDAPS endpoint.</summary>
public sealed class ActiveDirectoryLdapConnectionFactory
{
    /// <summary>The synthetic result code for a rejected server certificate.</summary>
    /// <remarks>Negative so it never collides with RFC 4511 result codes.</remarks>
    internal const int TlsValidationFailedResultCode = -2;

    /// <summary>The validated Active Directory connection settings.</summary>
    private readonly ActiveDirectoryOptions _options;

    /// <summary>Provides the current LDAP server trust anchors for each new connection.</summary>
    private readonly ITrustedCertificateStore _trustStore;

    /// <summary>Resolves domain controllers through DNS SRV records.</summary>
    private readonly IActiveDirectoryServerResolver _serverResolver;

    /// <summary>Reads the current forest-domain override for each new forest connection.</summary>
    private readonly IActiveDirectoryConfigurationStore _configurationStore;

    /// <summary>Writes connection diagnostics without credentials.</summary>
    private readonly ILogger<ActiveDirectoryLdapConnectionFactory> _logger;

    /// <summary>Initializes a new instance of the <see cref="ActiveDirectoryLdapConnectionFactory"/> class.</summary>
    /// <param name="options">The validated Active Directory connection settings.</param>
    /// <param name="serverResolver">The DNS SRV domain-controller resolver.</param>
    /// <param name="logger">The structured logger.</param>
    /// <param name="configurationStore">The shared dynamic forest-domain settings.</param>
    /// <param name="trustStore">The runtime LDAP trust anchors.</param>
    public ActiveDirectoryLdapConnectionFactory(
        IOptions<ActiveDirectoryOptions> options,
        IActiveDirectoryServerResolver serverResolver,
        ILogger<ActiveDirectoryLdapConnectionFactory> logger,
        IActiveDirectoryConfigurationStore configurationStore,
        ITrustedCertificateStore trustStore)
    {
        _options = options.Value;
        _trustStore = trustStore;
        _serverResolver = serverResolver;
        _logger = logger;
        _configurationStore = configurationStore;
    }

    /// <summary>Creates and binds a connection, disposing it if the bind fails.</summary>
    /// <remarks>
    /// A configured server is used directly; otherwise domain controllers are discovered through DNS
    /// SRV. Every server is tried over LDAPS first. Only when no LDAPS endpoint is usable and
    /// <see cref="ActiveDirectoryOptions.AllowLdapFallback"/> is enabled are the servers retried with
    /// LDAP upgraded through StartTLS. Connectivity errors and rejected server certificates move on
    /// to the next server; credential errors stop the search. Credentials are sent only after the
    /// server certificate has been validated.
    /// </remarks>
    /// <param name="credential">The directory credential used for the bind.</param>
    /// <param name="server">The optional AD DNS domain; the configured domain is used when omitted.</param>
    /// <param name="cancellationToken">Cancels connection and bind operations.</param>
    /// <param name="attempts">Optionally receives every connection attempt for diagnostics.</param>
    /// <returns>An authenticated connection owned by the caller.</returns>
    /// <exception cref="ActiveDirectoryLdapException">No secure endpoint could be bound.</exception>
    internal async Task<IActiveDirectoryLdapConnection> CreateBoundConnectionAsync(
        StoredDirectoryCredential credential,
        string? server,
        CancellationToken cancellationToken,
        ICollection<ActiveDirectoryLdapConnectionAttempt>? attempts = null)
    {
        var targetDomain = server ?? (await _configurationStore.GetAsync(cancellationToken)).Domain;
        var servers = string.IsNullOrWhiteSpace(_options.Server)
            ? await _serverResolver.ResolveAsync(targetDomain, cancellationToken)
            : [_options.Server!];
        var trustedCertificates = await _trustStore.GetTrustAnchorsAsync(cancellationToken);

        ActiveDirectoryLdapException? lastException = null;
        var transports = _options.AllowLdapFallback
            ? new[] { (ActiveDirectoryLdapTransport.Ldaps, _options.Port), (ActiveDirectoryLdapTransport.StartTls, _options.LdapFallbackPort) }
            : [(ActiveDirectoryLdapTransport.Ldaps, _options.Port)];
        foreach (var (transport, port) in transports)
        {
            foreach (var targetServer in servers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    _logger.LogInformation(
                        "Connecting to Active Directory server {Server} with {Transport} on port {Port}",
                        targetServer,
                        transport,
                        port);
                    var connection = await CreateBoundConnectionAsync(
                        credential,
                        targetServer,
                        port,
                        transport,
                        trustedCertificates,
                        cancellationToken);
                    attempts?.Add(new(targetServer, port, transport.ToString(), true, null, null));
                    return connection;
                }
                catch (ActiveDirectoryLdapException exception)
                {
                    attempts?.Add(new(
                        targetServer, port, transport.ToString(), false, exception.ResultCode, Describe(exception)));
                    if (!IsConnectionUnavailable(exception) &&
                        exception.ResultCode != TlsValidationFailedResultCode)
                    {
                        throw;
                    }

                    lastException = exception;
                    _logger.LogWarning(
                        exception,
                        "{Transport} connection to Active Directory server {Server}:{Port} failed: {Reason}",
                        transport,
                        targetServer,
                        port,
                        exception.Message);
                }
            }
        }

        throw lastException ?? new ActiveDirectoryLdapException(
            $"No secure LDAP endpoint was available for '{targetDomain}'.",
            81,
            new InvalidOperationException("DNS discovery returned no usable secure LDAP endpoint."));
    }

    /// <summary>Creates a TLS-protected LDAP connection.</summary>
    /// <param name="credential">The directory credential used for the bind.</param>
    /// <param name="cancellationToken">Cancels connection and bind operations.</param>
    /// <returns>An authenticated secure connection owned by the caller.</returns>
    internal Task<IActiveDirectoryLdapConnection> CreateSecureBoundConnectionAsync(
        StoredDirectoryCredential credential,
        CancellationToken cancellationToken) =>
        CreateBoundConnectionAsync(credential, server: null, cancellationToken);

    /// <summary>Combines an LDAP failure with its client-specific detail, without credentials.</summary>
    /// <param name="exception">The LDAP failure.</param>
    /// <returns>The diagnostic text.</returns>
    internal static string Describe(ActiveDirectoryLdapException exception)
    {
        var detail = exception.InnerException switch
        {
            NovellLdapException novell when !string.IsNullOrWhiteSpace(novell.LdapErrorMessage) =>
                $"{novell.Message} ({novell.LdapErrorMessage})",
            { } inner when exception.ResultCode != TlsValidationFailedResultCode => inner.Message,
            _ => null
        };
        return detail is null ? exception.Message : $"{exception.Message} {detail}";
    }

    /// <summary>Creates and binds one LDAP v3 connection using the selected transport mode.</summary>
    /// <param name="credential">The directory credential used for authentication.</param>
    /// <param name="server">The DNS name of the target domain controller or domain.</param>
    /// <param name="port">The LDAPS or LDAP endpoint port.</param>
    /// <param name="transport">Selects LDAPS or LDAP upgraded with StartTLS.</param>
    /// <param name="trustedCertificates">The trust anchors, or an empty collection for the OS root store.</param>
    /// <param name="cancellationToken">Cancels connection and bind operations.</param>
    /// <returns>A bound connection that must be disposed by the caller.</returns>
    private Task<IActiveDirectoryLdapConnection> CreateBoundConnectionAsync(
        StoredDirectoryCredential credential,
        string server,
        int port,
        ActiveDirectoryLdapTransport transport,
        IReadOnlyCollection<X509Certificate2> trustedCertificates,
        CancellationToken cancellationToken)
    {
        if (UsesNovellClient(transport, OperatingSystem.IsWindows()))
        {
            return CreateNovellConnectionAsync(
                credential,
                server,
                port,
                transport,
                trustedCertificates,
                cancellationToken);
        }

        return Task.FromResult(CreateSystemConnection(credential, server, port, transport, trustedCertificates));
    }

    /// <summary>Determines whether the Novell LDAP client must be used instead of System.DirectoryServices.Protocols.</summary>
    /// <remarks>
    /// Outside Windows, System.DirectoryServices.Protocols cannot enforce the custom server-certificate
    /// validation, so TLS-protected connections use the managed Novell client.
    /// </remarks>
    /// <param name="transport">The selected transport mode.</param>
    /// <param name="isWindows">Whether the process runs on Windows.</param>
    /// <returns><see langword="true"/> when the Novell client must be used.</returns>
    internal static bool UsesNovellClient(
        ActiveDirectoryLdapTransport transport,
        bool isWindows) =>
        !isWindows && transport is ActiveDirectoryLdapTransport.Ldaps or
            ActiveDirectoryLdapTransport.StartTls;

    /// <summary>Creates and binds a connection with System.DirectoryServices.Protocols.</summary>
    /// <param name="credential">The directory credential used for the simple bind.</param>
    /// <param name="server">The DNS name of the target domain controller.</param>
    /// <param name="port">The LDAPS or LDAP endpoint port.</param>
    /// <param name="transport">Selects LDAPS or LDAP upgraded with StartTLS.</param>
    /// <param name="trustedCertificates">The trust anchors, or an empty collection for the OS root store.</param>
    /// <returns>A bound connection that must be disposed by the caller.</returns>
    /// <exception cref="ActiveDirectoryLdapException">The TLS negotiation or bind failed.</exception>
    private IActiveDirectoryLdapConnection CreateSystemConnection(
        StoredDirectoryCredential credential,
        string server,
        int port,
        ActiveDirectoryLdapTransport transport,
        IReadOnlyCollection<X509Certificate2> trustedCertificates)
    {
        var identifier = new LdapDirectoryIdentifier(server, port);
        var networkCredential = new NetworkCredential(credential.Username, credential.Password);
        string? rejectionReason = null;
        var connection = new LdapConnection(identifier, networkCredential, AuthType.Basic)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.SecureSocketLayer =
            transport == ActiveDirectoryLdapTransport.Ldaps;
        if (OperatingSystem.IsWindows())
        {
            connection.SessionOptions.VerifyServerCertificate = (_, certificate) =>
            {
                var valid = ValidateServerCertificate(server, certificate, trustedCertificates, out var reason);
                rejectionReason ??= reason;
                return valid;
            };
        }

        try
        {
            if (transport == ActiveDirectoryLdapTransport.StartTls)
            {
                connection.SessionOptions.StartTransportLayerSecurity(null);
            }
            connection.Bind();
            return new SystemDirectoryServicesLdapConnection(connection);
        }
        catch (Exception exception) when (rejectionReason is not null && exception is not OperationCanceledException)
        {
            connection.Dispose();
            throw TlsValidationFailed(server, rejectionReason, exception);
        }
        catch (LdapException exception)
        {
            connection.Dispose();
            throw new ActiveDirectoryLdapException(
                "The Active Directory LDAP bind failed.",
                exception.ErrorCode,
                exception);
        }
    }

    /// <summary>Creates and binds a connection with the managed Novell LDAP client.</summary>
    /// <param name="credential">The directory credential used for the simple bind.</param>
    /// <param name="server">The DNS name of the target domain controller.</param>
    /// <param name="port">The LDAPS or LDAP endpoint port.</param>
    /// <param name="transport">Selects LDAPS or LDAP upgraded with StartTLS.</param>
    /// <param name="trustedCertificates">The trust anchors, or an empty collection for the OS root store.</param>
    /// <param name="cancellationToken">Cancels connection, TLS negotiation, and bind operations.</param>
    /// <returns>A bound connection that must be disposed by the caller.</returns>
    /// <exception cref="ActiveDirectoryLdapException">The connection, TLS negotiation, or bind failed.</exception>
    private async Task<IActiveDirectoryLdapConnection> CreateNovellConnectionAsync(
        StoredDirectoryCredential credential,
        string server,
        int port,
        ActiveDirectoryLdapTransport transport,
        IReadOnlyCollection<X509Certificate2> trustedCertificates,
        CancellationToken cancellationToken)
    {
        string? rejectionReason = null;
        var options = CreateNovellOptions(
            server,
            trustedCertificates,
            transport == ActiveDirectoryLdapTransport.Ldaps,
            reason => rejectionReason ??= reason);
        var connection = new NovellLdapConnection(options)
        {
            ConnectionTimeout = 30_000
        };

        try
        {
            await connection.ConnectAsync(server, port, cancellationToken);
            if (transport == ActiveDirectoryLdapTransport.StartTls)
            {
                await connection.StartTlsAsync(cancellationToken);
            }
            await connection.BindAsync(
                credential.Username,
                credential.Password,
                cancellationToken);
            return new NovellActiveDirectoryLdapConnection(connection);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException &&
            (rejectionReason is not null || exception is AuthenticationException))
        {
            connection.Dispose();
            throw TlsValidationFailed(server, rejectionReason ?? exception.Message, exception);
        }
        catch (NovellLdapException exception)
        {
            connection.Dispose();
            throw new ActiveDirectoryLdapException(
                "The Active Directory TLS-protected LDAP bind failed.",
                exception.ResultCode,
                exception);
        }
    }

    /// <summary>Creates the exception for a rejected server certificate.</summary>
    /// <param name="server">The domain controller DNS name.</param>
    /// <param name="reason">The certificate validation failure.</param>
    /// <param name="innerException">The TLS client exception.</param>
    /// <returns>The LDAP exception with <see cref="TlsValidationFailedResultCode"/>.</returns>
    private static ActiveDirectoryLdapException TlsValidationFailed(
        string server, string reason, Exception innerException) =>
        new($"TLS validation of the certificate presented by '{server}' failed: {reason}",
            TlsValidationFailedResultCode,
            innerException);

    /// <summary>Creates Novell connection options with strict server-certificate validation.</summary>
    /// <param name="server">The DNS name the server certificate must match.</param>
    /// <param name="trustedCertificates">The trust anchors, or an empty collection for the OS root store.</param>
    /// <param name="useSsl">Whether the connection uses LDAPS from the start.</param>
    /// <param name="reportRejection">Receives the reason when the certificate is rejected.</param>
    /// <returns>The configured connection options.</returns>
    private static NovellLdapConnectionOptions CreateNovellOptions(
        string server,
        IReadOnlyCollection<X509Certificate2> trustedCertificates,
        bool useSsl,
        Action<string> reportRejection)
    {
        var options = new NovellLdapConnectionOptions()
            .ConfigureRemoteCertificateValidationCallback(
                (_, certificate, _, _) =>
                {
                    if (certificate is null)
                    {
                        reportRejection("The server presented no certificate.");
                        return false;
                    }
                    if (!ValidateServerCertificate(server, certificate, trustedCertificates, out var reason))
                    {
                        reportRejection(reason!);
                        return false;
                    }
                    return true;
                });
        return useSsl ? options.UseSsl() : options;
    }

    /// <summary>Loads the optional installer-packaged DER or PEM trust anchors.</summary>
    /// <remarks>
    /// The packaged certificate seeds <see cref="BlobTrustedCertificateStore"/> until trust anchors
    /// are managed at runtime. Relative paths resolve against the application base directory.
    /// Certificates with private keys are ignored when reading PEM content.
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

    /// <summary>Identifies connectivity errors for which an authenticated LDAP fallback is safe.</summary>
    /// <remarks>
    /// Credential and authorization failures are deliberately excluded so invalid credentials do
    /// not weaken the transport selected by the caller.
    /// </remarks>
    /// <param name="exception">The LDAP failure to classify.</param>
    /// <returns><see langword="true"/> for busy, unavailable, server-down, timeout, and connect errors.</returns>
    private static bool IsConnectionUnavailable(ActiveDirectoryLdapException exception) =>
        exception.ResultCode is 51 or 52 or 81 or 85 or 91;

    /// <summary>Validates the LDAP server certificate against a configured private trust anchor.</summary>
    /// <remarks>
    /// The certificate must match <paramref name="server"/>, chain to a trusted root, and carry the
    /// server-authentication EKU. Chains with intermediates are additionally checked online for
    /// revocation; a self-signed trust anchor presented directly is accepted without revocation.
    /// </remarks>
    /// <param name="server">The DNS name that was connected to.</param>
    /// <param name="certificate">The certificate presented by the server.</param>
    /// <param name="trustedCertificates">The packaged trust anchors, or an empty collection for the OS root store.</param>
    /// <returns><see langword="true"/> when the certificate is trusted for the server.</returns>
    internal static bool ValidateServerCertificate(
        string server,
        X509Certificate certificate,
        IReadOnlyCollection<X509Certificate2> trustedCertificates) =>
        ValidateServerCertificate(server, certificate, trustedCertificates, out _);

    /// <summary>Validates the LDAP server certificate and explains a rejection.</summary>
    /// <param name="server">The DNS name that was connected to.</param>
    /// <param name="certificate">The certificate presented by the server.</param>
    /// <param name="trustedCertificates">The trust anchors, or an empty collection for the OS root store.</param>
    /// <param name="failureReason">The rejection reason, or null when the certificate is trusted.</param>
    /// <returns><see langword="true"/> when the certificate is trusted for the server.</returns>
    internal static bool ValidateServerCertificate(
        string server,
        X509Certificate certificate,
        IReadOnlyCollection<X509Certificate2> trustedCertificates,
        out string? failureReason)
    {
        using var serverCertificate = new X509Certificate2(
            certificate.Export(X509ContentType.Cert));
        if (!MatchesDnsName(serverCertificate, server))
        {
            var names = GetDnsNames(serverCertificate);
            failureReason =
                $"The certificate names ({(names.Count == 0 ? "none" : string.Join(", ", names))}) do not match the server name '{server}'.";
            return false;
        }

        using var trustChain = CreateCertificateChain(
            trustedCertificates,
            X509RevocationMode.NoCheck);
        if (!trustChain.Build(serverCertificate))
        {
            var anchors = trustedCertificates.Count == 0
                ? " No trust anchor is configured, so the operating-system root store was used."
                : $" Configured trust anchors: {string.Join("; ", trustedCertificates.Select(anchor => anchor.Subject))}.";
            failureReason =
                $"The certificate '{serverCertificate.Subject}' issued by '{serverCertificate.Issuer}' is not trusted: {DescribeChainStatus(trustChain)}.{anchors}";
            return false;
        }
        if (trustChain.ChainElements.Count == 1)
        {
            failureReason = null;
            return true;
        }

        using var revocationChain = CreateCertificateChain(
            trustedCertificates,
            X509RevocationMode.Online);
        if (!revocationChain.Build(serverCertificate))
        {
            failureReason =
                $"The revocation check for '{serverCertificate.Subject}' failed: {DescribeChainStatus(revocationChain)}. " +
                "The CRL distribution points or OCSP responders of the chain must be reachable from the Function App's virtual network.";
            return false;
        }

        failureReason = null;
        return true;
    }

    /// <summary>Summarizes the distinct chain status entries.</summary>
    /// <param name="chain">The built chain.</param>
    /// <returns>The status names and descriptions.</returns>
    private static string DescribeChainStatus(X509Chain chain)
    {
        var statuses = chain.ChainStatus
            .Select(status => string.IsNullOrWhiteSpace(status.StatusInformation)
                ? status.Status.ToString()
                : $"{status.Status} ({status.StatusInformation.Trim()})")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return statuses.Length == 0 ? "unknown chain error" : string.Join("; ", statuses);
    }

    /// <summary>Creates an X.509 chain policy for LDAP server-certificate validation.</summary>
    /// <param name="trustedCertificates">The packaged trust anchors, or an empty collection for the OS root store.</param>
    /// <param name="revocationMode">The revocation checking mode.</param>
    /// <returns>A chain that requires the server-authentication EKU and must be disposed by the caller.</returns>
    private static X509Chain CreateCertificateChain(
        IReadOnlyCollection<X509Certificate2> trustedCertificates,
        X509RevocationMode revocationMode)
    {
        var chain = new X509Chain();
        // Without a packaged trust anchor, the operating system root store applies, which
        // supports LDAPS certificates issued by a public CA.
        if (trustedCertificates.Count > 0)
        {
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.AddRange(trustedCertificates.ToArray());
        }
        chain.ChainPolicy.RevocationMode = revocationMode;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
        chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(10);
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        return chain;
    }

    /// <summary>Checks the certificate DNS identity, including a single-label wildcard.</summary>
    /// <remarks>
    /// Every subject alternative DNS name is considered, as required by RFC 6125. Domain controller
    /// certificates often list the DC host name together with the domain name.
    /// </remarks>
    /// <param name="certificate">The server certificate.</param>
    /// <param name="server">The DNS name that was connected to.</param>
    /// <returns><see langword="true"/> when a certificate DNS name matches the server.</returns>
    internal static bool MatchesDnsName(X509Certificate2 certificate, string server) =>
        GetDnsNames(certificate).Any(name => MatchesDnsName(name, server));

    /// <summary>Gets the subject alternative DNS names, or the subject DNS name when none exist.</summary>
    /// <param name="certificate">The server certificate.</param>
    /// <returns>The DNS names presented by the certificate.</returns>
    internal static IReadOnlyList<string> GetDnsNames(X509Certificate2 certificate)
    {
        var names = new List<string>();
        foreach (var extension in certificate.Extensions)
        {
            if (extension.Oid?.Value == "2.5.29.17")
            {
                names.AddRange(new X509SubjectAlternativeNameExtension(extension.RawData, extension.Critical)
                    .EnumerateDnsNames());
            }
        }
        if (names.Count == 0)
        {
            var subjectName = certificate.GetNameInfo(X509NameType.DnsName, forIssuer: false);
            if (!string.IsNullOrWhiteSpace(subjectName))
            {
                names.Add(subjectName);
            }
        }
        return names;
    }

    /// <summary>Compares one certificate DNS name with the server name.</summary>
    /// <param name="certificateName">The certificate DNS name, optionally with a leading wildcard label.</param>
    /// <param name="server">The DNS name that was connected to.</param>
    /// <returns><see langword="true"/> when the names match.</returns>
    private static bool MatchesDnsName(string certificateName, string server)
    {
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

/// <summary>Selects how an LDAP connection is protected with TLS.</summary>
internal enum ActiveDirectoryLdapTransport
{
    /// <summary>LDAP over TLS from connection establishment (LDAPS).</summary>
    Ldaps,

    /// <summary>Plain LDAP upgraded to TLS with the StartTLS extended operation before binding.</summary>
    StartTls
}
