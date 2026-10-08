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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NovellLdapConnection = Novell.Directory.Ldap.LdapConnection;
using NovellLdapConnectionOptions = Novell.Directory.Ldap.LdapConnectionOptions;
using NovellLdapException = Novell.Directory.Ldap.LdapException;

namespace HybridDeviceTagger.Services;

/// <summary>Creates authenticated LDAP v3 connections using the configured LDAPS endpoint.</summary>
public sealed class ActiveDirectoryLdapConnectionFactory
{
    private readonly ActiveDirectoryOptions _options;
    private readonly IActiveDirectoryTrustStore _trustStore;
    private readonly IActiveDirectoryServerResolver _serverResolver;
    private readonly ILogger<ActiveDirectoryLdapConnectionFactory> _logger;

    /// <summary>Initializes a new instance of the <see cref="ActiveDirectoryLdapConnectionFactory"/> class.</summary>
    /// <param name="options">The validated Active Directory connection settings.</param>
    /// <param name="trustStore">The dynamic LDAPS trust-certificate provider.</param>
    /// <param name="serverResolver">The DNS SRV domain-controller resolver.</param>
    /// <param name="logger">The structured logger.</param>
    public ActiveDirectoryLdapConnectionFactory(
        IOptions<ActiveDirectoryOptions> options,
        IActiveDirectoryTrustStore trustStore,
        IActiveDirectoryServerResolver serverResolver,
        ILogger<ActiveDirectoryLdapConnectionFactory> logger)
    {
        _options = options.Value;
        _trustStore = trustStore;
        _serverResolver = serverResolver;
        _logger = logger;
    }

    /// <summary>Creates and binds a connection, disposing it if the bind fails.</summary>
    /// <param name="credential">The directory credential used for the bind.</param>
    /// <param name="server">The optional AD DNS domain; the configured domain is used when omitted.</param>
    /// <param name="cancellationToken">Cancels connection and bind operations.</param>
    /// <returns>An authenticated connection owned by the caller.</returns>
    internal async Task<IActiveDirectoryLdapConnection> CreateBoundConnectionAsync(
        StoredDirectoryCredential credential,
        string? server,
        CancellationToken cancellationToken)
    {
        var targetDomain = server ?? _options.Domain;
        var servers = string.IsNullOrWhiteSpace(_options.Server)
            ? await _serverResolver.ResolveAsync(targetDomain, cancellationToken)
            : [_options.Server!];

        ActiveDirectoryLdapException? lastException = null;
        foreach (var targetServer in servers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _logger.LogInformation(
                    "Connecting to discovered Active Directory server {Server} on LDAPS port {Port}",
                    targetServer,
                    _options.Port);
                return await CreateBoundConnectionAsync(
                    credential,
                    targetServer,
                    _options.Port,
                    ActiveDirectoryLdapTransport.Ldaps,
                    cancellationToken);
            }
            catch (ActiveDirectoryLdapException exception) when (IsConnectionUnavailable(exception))
            {
                lastException = exception;
                _logger.LogWarning(
                    exception,
                    "LDAPS connection to discovered server {Server}:{Port} failed",
                    targetServer,
                    _options.Port);
            }
        }

        if (!_options.AllowLdapFallback)
        {
            throw lastException ?? new ActiveDirectoryLdapException(
                $"No LDAPS endpoint was available for '{targetDomain}'.",
                81,
                new InvalidOperationException("DNS discovery returned no usable LDAPS endpoint."));
        }

        foreach (var targetServer in servers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _logger.LogInformation(
                    "Connecting to discovered Active Directory server {Server} with StartTLS on LDAP port {Port}",
                    targetServer,
                    _options.LdapFallbackPort);
                return await CreateBoundConnectionAsync(
                    credential,
                    targetServer,
                    _options.LdapFallbackPort,
                    ActiveDirectoryLdapTransport.StartTls,
                    cancellationToken);
            }
            catch (ActiveDirectoryLdapException exception) when (IsConnectionUnavailable(exception))
            {
                lastException = exception;
                _logger.LogWarning(
                    exception,
                    "LDAP StartTLS connection to discovered server {Server}:{Port} failed",
                    targetServer,
                    _options.LdapFallbackPort);
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

    /// <summary>Creates and binds one LDAP v3 connection using the selected transport mode.</summary>
    /// <param name="credential">The directory credential used for authentication.</param>
    /// <param name="server">The DNS name of the target domain controller or domain.</param>
    /// <param name="port">The LDAPS or LDAP endpoint port.</param>
    /// <param name="transport">Selects LDAPS or LDAP upgraded with StartTLS.</param>
    /// <param name="cancellationToken">Cancels connection and bind operations.</param>
    /// <returns>A bound connection that must be disposed by the caller.</returns>
    private Task<IActiveDirectoryLdapConnection> CreateBoundConnectionAsync(
        StoredDirectoryCredential credential,
        string server,
        int port,
        ActiveDirectoryLdapTransport transport,
        CancellationToken cancellationToken)
    {
        if (UsesNovellClient(transport, OperatingSystem.IsWindows()))
        {
            return CreateNovellConnectionAsync(
                credential,
                server,
                port,
                transport,
                cancellationToken);
        }

        return Task.FromResult(CreateSystemConnection(credential, server, port, transport));
    }

    internal static bool UsesNovellClient(
        ActiveDirectoryLdapTransport transport,
        bool isWindows) =>
        !isWindows && transport is ActiveDirectoryLdapTransport.Ldaps or
            ActiveDirectoryLdapTransport.StartTls;

    private IActiveDirectoryLdapConnection CreateSystemConnection(
        StoredDirectoryCredential credential,
        string server,
        int port,
        ActiveDirectoryLdapTransport transport)
    {
        var identifier = new LdapDirectoryIdentifier(server, port);
        var networkCredential = new NetworkCredential(credential.Username, credential.Password);
        var trustedCertificates = _trustStore.GetCertificates();
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
            ValidateServerCertificate(server, certificate, trustedCertificates);
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
        catch (LdapException exception)
        {
            connection.Dispose();
            throw new ActiveDirectoryLdapException(
                "The Active Directory LDAP bind failed.",
                exception.ErrorCode,
                exception);
        }
    }

    private async Task<IActiveDirectoryLdapConnection> CreateNovellConnectionAsync(
        StoredDirectoryCredential credential,
        string server,
        int port,
        ActiveDirectoryLdapTransport transport,
        CancellationToken cancellationToken)
    {
        var trustedCertificates = _trustStore.GetCertificates();
        var options = CreateNovellOptions(
            server,
            trustedCertificates,
            transport == ActiveDirectoryLdapTransport.Ldaps);
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
        catch (NovellLdapException exception)
        {
            connection.Dispose();
            throw new ActiveDirectoryLdapException(
                "The Active Directory TLS-protected LDAP bind failed.",
                exception.ResultCode,
                exception);
        }
    }

    private static NovellLdapConnectionOptions CreateNovellOptions(
        string server,
        IReadOnlyCollection<X509Certificate2> trustedCertificates,
        bool useSsl)
    {
        var options = new NovellLdapConnectionOptions()
            .ConfigureRemoteCertificateValidationCallback(
                (_, certificate, _, _) =>
                    certificate is not null &&
                    ValidateServerCertificate(server, certificate, trustedCertificates));
        return useSsl ? options.UseSsl() : options;
    }

    /// <summary>Identifies connectivity errors for which an authenticated LDAP fallback is safe.</summary>
    /// <remarks>
    /// Credential and authorization failures are deliberately excluded so invalid credentials do
    /// not weaken the transport selected by the caller.
    /// </remarks>
    private static bool IsConnectionUnavailable(ActiveDirectoryLdapException exception) =>
        exception.ResultCode is 51 or 52 or 81 or 85 or 91;

    /// <summary>Validates the LDAP server certificate against a configured private trust anchor.</summary>
    internal static bool ValidateServerCertificate(
        string server,
        X509Certificate certificate,
        IReadOnlyCollection<X509Certificate2> trustedCertificates)
    {
        using var serverCertificate = new X509Certificate2(
            certificate.Export(X509ContentType.Cert));
        if (!MatchesDnsName(serverCertificate, server))
        {
            return false;
        }

        using var trustChain = CreateCertificateChain(
            trustedCertificates,
            X509RevocationMode.NoCheck);
        if (!trustChain.Build(serverCertificate))
        {
            return false;
        }
        if (trustChain.ChainElements.Count == 1)
        {
            return true;
        }

        using var revocationChain = CreateCertificateChain(
            trustedCertificates,
            X509RevocationMode.Online);
        return revocationChain.Build(serverCertificate);
    }

    private static X509Chain CreateCertificateChain(
        IReadOnlyCollection<X509Certificate2> trustedCertificates,
        X509RevocationMode revocationMode)
    {
        var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(trustedCertificates.ToArray());
        chain.ChainPolicy.RevocationMode = revocationMode;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
        chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(10);
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        return chain;
    }

    /// <summary>Checks the certificate DNS identity, including a single-label wildcard.</summary>
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

internal enum ActiveDirectoryLdapTransport
{
    Ldaps,
    StartTls
}