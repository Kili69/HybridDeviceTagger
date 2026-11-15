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

using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Services;

/// <summary>Discovers hybrid-joined computers through paged LDAP searches.</summary>
public sealed class LdapActiveDirectoryDeviceSource :
    IActiveDirectoryDeviceSource,
    IActiveDirectoryRuleValidator
{
    /// <summary>Selects computer objects with a populated <c>userCertificate</c>, as synchronized by Entra Connect.</summary>
    internal const string ComputerFilter =
        "(&(objectCategory=computer)(userCertificate=*))";

    /// <summary>The binary AD attribute that equals the Entra <c>deviceId</c>.</summary>
    internal const string DeviceIdAttribute = "objectGUID";

    /// <summary>The LDAP search settings.</summary>
    private readonly ActiveDirectoryOptions _options;

    /// <summary>The active and staged credential store.</summary>
    private readonly IActiveDirectoryCredentialStore _credentialStore;

    /// <summary>The authenticated LDAP connection factory.</summary>
    private readonly ActiveDirectoryLdapConnectionFactory _connectionFactory;

    /// <summary>The structured logger.</summary>
    private readonly ILogger<LdapActiveDirectoryDeviceSource> _logger;

    /// <summary>Initializes a new instance of the <see cref="LdapActiveDirectoryDeviceSource"/> class.</summary>
    /// <param name="options">The LDAP search settings.</param>
    /// <param name="credentialStore">The active and staged credential store.</param>
    /// <param name="connectionFactory">The authenticated LDAP connection factory.</param>
    /// <param name="logger">The structured logger.</param>
    public LdapActiveDirectoryDeviceSource(
        IOptions<ActiveDirectoryOptions> options,
        IActiveDirectoryCredentialStore credentialStore,
        ActiveDirectoryLdapConnectionFactory connectionFactory,
        ILogger<LdapActiveDirectoryDeviceSource> logger)
    {
        _options = options.Value;
        _credentialStore = credentialStore;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// OU-only paths are resolved through the forest configuration partition and searched in
    /// every domain. Queries use server-side paging, read binary object GUID values, and
    /// fall back to the staged credential when the active credential can no longer bind.
    /// </remarks>
    public async Task<IReadOnlyList<DirectoryDevice>> GetHybridJoinedDevicesAsync(
        string baseDn,
        CancellationToken cancellationToken)
    {
        var forestDomains = await GetForestDomainsAsync(cancellationToken);
        var isForestRelative = ActiveDirectorySearchPath.IsForestRelative(baseDn);
        var searchTargets = isForestRelative
            ? GetForestSearchTargets(baseDn, forestDomains)
            : [GetDomainSearchTarget(baseDn, forestDomains)];
        var devices = new Dictionary<Guid, DirectoryDevice>();
        var resolvedOuCount = 0;

        foreach (var target in searchTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var connection = await CreateConnectionAsync(target.Server, cancellationToken);
            try
            {
                foreach (var device in await SearchDevicesAsync(
                    connection,
                    target.BaseDn,
                    cancellationToken))
                {
                    devices[device.DeviceId] = device;
                }
                resolvedOuCount++;
            }
            catch (ActiveDirectoryLdapException exception) when (
                isForestRelative && exception.ResultCode == 32)
            {
                _logger.LogInformation(
                    "OU path {OuPath} does not exist in forest domain {Domain}; skipping that domain.",
                    baseDn,
                    target.Server);
            }
        }

        if (isForestRelative && resolvedOuCount == 0)
        {
            throw new InvalidOperationException(
                $"OU path '{baseDn}' was not found in any domain in the forest.");
        }

        return devices.Values.ToArray();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Forest-relative OU paths are intrinsically constrained because their targets are built only
    /// from discovered forest domains. Every domain-specific path must exactly match one advertised
    /// <c>dnsRoot</c> before the configuration can be persisted.
    /// </remarks>
    public async Task ValidateRulesAsync(
        IReadOnlyList<TaggingRuleOptions> rules,
        CancellationToken cancellationToken)
    {
        var domainSpecificRules = rules
            .Where(rule => !ActiveDirectorySearchPath.IsForestRelative(rule.BaseDn))
            .ToArray();
        if (domainSpecificRules.Length == 0)
        {
            return;
        }

        var forestDomains = await GetForestDomainsAsync(cancellationToken);
        var errors = domainSpecificRules
            .Where(rule => !IsForestDomain(
                rule.BaseDn,
                forestDomains.Select(domain => domain.DnsRoot)))
            .Select(rule =>
                $"Tagging rule '{rule.Name}' BaseDn targets domain " +
                $"'{ActiveDirectorySearchPath.GetDomainDnsName(rule.BaseDn)}', which is not " +
                "advertised by the configured Active Directory forest.")
            .ToArray();
        if (errors.Length > 0)
        {
            throw new TaggingConfigurationValidationException(errors);
        }
    }

    /// <summary>Runs a paged subtree search and projects computers with valid hybrid device IDs.</summary>
    /// <remarks>
    /// Microsoft Entra Connect identifies synchronized domain-joined computers by a populated
    /// <c>userCertificate</c> and maps their AD <c>objectGUID</c> to the Entra <c>deviceId</c>.
    /// </remarks>
    /// <param name="connection">The bound LDAP connection.</param>
    /// <param name="baseDn">The complete search base distinguished name.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>The computers that carry a valid 16-byte device ID.</returns>
    private async Task<IReadOnlyList<DirectoryDevice>> SearchDevicesAsync(
        IActiveDirectoryLdapConnection connection,
        string baseDn,
        CancellationToken cancellationToken)
    {
        var entries = await connection.SearchAsync(
            baseDn,
            ComputerFilter,
            ActiveDirectoryLdapSearchScope.Subtree,
            [DeviceIdAttribute, "name", "distinguishedName"],
            _options.PageSize,
            cancellationToken);
        return entries
            .Select(entry => TryReadDevice(entry, out var device) ? device : null)
            .Where(device => device is not null)
            .Cast<DirectoryDevice>()
            .ToArray();
    }

    /// <summary>Reads every domain advertised by the configured forest.</summary>
    /// <remarks>
    /// Discovery always starts through the configured server, never through a server name derived
    /// from a rule. Domain <c>crossRef</c> objects are read from the configuration partition.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the LDAP searches.</param>
    /// <returns>The forest domains ordered by DNS root.</returns>
    /// <exception cref="InvalidOperationException">The forest returned no configuration naming context or no domains.</exception>
    private async Task<IReadOnlyList<ForestDomain>> GetForestDomainsAsync(
        CancellationToken cancellationToken)
    {
        using var connection = await CreateConnectionAsync(_options.Domain, cancellationToken);
        var rootResponse = await connection.SearchAsync(
            string.Empty,
            "(objectClass=*)",
            ActiveDirectoryLdapSearchScope.Base,
            ["configurationNamingContext"],
            pageSize: null,
            cancellationToken);
        var configurationNamingContext = rootResponse[0].GetString(
            "configurationNamingContext")
            ?? throw new InvalidOperationException(
                "Active Directory did not return a configuration naming context.");
        var partitionsDn = $"CN=Partitions,{configurationNamingContext}";
        var domainsResponse = await connection.SearchAsync(
            partitionsDn,
            "(&(objectClass=crossRef)(systemFlags:1.2.840.113556.1.4.803:=3))",
            ActiveDirectoryLdapSearchScope.OneLevel,
            ["nCName", "dnsRoot"],
            pageSize: null,
            cancellationToken);

        var domains = domainsResponse
            .Select(entry => new
            {
                NamingContext = entry.GetString("nCName"),
                DnsRoot = entry.GetString("dnsRoot")
            })
            .Where(domain =>
                !string.IsNullOrWhiteSpace(domain.NamingContext) &&
                !string.IsNullOrWhiteSpace(domain.DnsRoot))
            .Select(domain => new ForestDomain(domain.DnsRoot!, domain.NamingContext!))
            .OrderBy(domain => domain.DnsRoot, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (domains.Length == 0)
        {
            throw new InvalidOperationException(
                "Active Directory did not advertise any domains in the forest.");
        }

        return domains;
    }

    /// <summary>Builds safe targets for an OU-only path from discovered forest domains.</summary>
    /// <param name="ouPath">The forest-relative OU path.</param>
    /// <param name="forestDomains">The discovered forest domains.</param>
    /// <returns>One search target per forest domain.</returns>
    private IReadOnlyList<SearchTarget> GetForestSearchTargets(
        string ouPath,
        IReadOnlyList<ForestDomain> forestDomains) =>
        forestDomains
            .Select(domain => new SearchTarget(
                GetLdapServer(domain.DnsRoot),
                ActiveDirectorySearchPath.CombineWithDomain(ouPath, domain.NamingContext)))
            .ToArray();

    /// <summary>Resolves a domain-specific path only when its domain belongs to the forest.</summary>
    /// <param name="baseDn">The domain-specific search base distinguished name.</param>
    /// <param name="forestDomains">The discovered forest domains.</param>
    /// <returns>The search target for the matching forest domain.</returns>
    /// <exception cref="TaggingConfigurationValidationException">The path targets a domain outside the forest.</exception>
    private SearchTarget GetDomainSearchTarget(
        string baseDn,
        IReadOnlyList<ForestDomain> forestDomains)
    {
        var domainDnsName = ActiveDirectorySearchPath.GetDomainDnsName(baseDn);
        var forestDomain = forestDomains.FirstOrDefault(domain =>
            string.Equals(domain.DnsRoot, domainDnsName, StringComparison.OrdinalIgnoreCase));
        if (forestDomain is null)
        {
            throw new TaggingConfigurationValidationException(
            [
                $"BaseDn targets domain '{domainDnsName}', which is not advertised by the " +
                "configured Active Directory forest."
            ]);
        }

        return new SearchTarget(GetLdapServer(forestDomain.DnsRoot), baseDn);
    }

    /// <summary>Checks a domain-specific path against the exact discovered DNS roots.</summary>
    /// <param name="baseDn">The domain-specific search base distinguished name.</param>
    /// <param name="forestDomainDnsRoots">The DNS roots advertised by the forest.</param>
    /// <returns><see langword="true"/> when the path's domain matches a DNS root case-insensitively.</returns>
    internal static bool IsForestDomain(
        string baseDn,
        IEnumerable<string> forestDomainDnsRoots)
    {
        var domainDnsName = ActiveDirectorySearchPath.GetDomainDnsName(baseDn);
        return forestDomainDnsRoots.Any(dnsRoot =>
            string.Equals(dnsRoot, domainDnsName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Returns the domain whose domain controllers must be discovered through DNS.</summary>
    /// <param name="domainDnsName">The forest domain DNS root.</param>
    /// <returns>The DNS domain passed to the connection factory.</returns>
    private static string GetLdapServer(string domainDnsName) => domainDnsName;

    /// <summary>Creates a connection with automatic staged-credential recovery.</summary>
    /// <remarks>
    /// The active credential is tried first. If its bind fails during an interrupted rotation,
    /// the staged <c>next</c> credential is loaded and tried against the same server.
    /// </remarks>
    /// <param name="server">The AD DNS domain whose domain controllers are contacted.</param>
    /// <param name="cancellationToken">Cancels credential loading and the bind.</param>
    /// <returns>A bound connection that must be disposed by the caller.</returns>
    /// <exception cref="ActiveDirectoryLdapException">Neither the active nor a staged credential could bind.</exception>
    private async Task<IActiveDirectoryLdapConnection> CreateConnectionAsync(
        string server,
        CancellationToken cancellationToken)
    {
        var current = await _credentialStore.GetCurrentAsync(cancellationToken);
        try
        {
            return await _connectionFactory.CreateBoundConnectionAsync(
                current,
                server,
                cancellationToken);
        }
        catch (ActiveDirectoryLdapException)
        {
            var next = await _credentialStore.GetNextAsync(cancellationToken);
            if (next is null)
            {
                throw;
            }

            return await _connectionFactory.CreateBoundConnectionAsync(
                next,
                server,
                cancellationToken);
        }
    }

    /// <summary>Projects one LDAP entry when it contains a valid hybrid device identifier.</summary>
    /// <param name="entry">The LDAP computer entry.</param>
    /// <param name="device">The projected device when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the entry has a 16-byte <c>objectGUID</c>.</returns>
    private static bool TryReadDevice(
        ActiveDirectoryLdapEntry entry,
        out DirectoryDevice device)
    {
        device = default!;
        if (!TryReadGuid(entry.GetBytes(DeviceIdAttribute), out var deviceId))
        {
            return false;
        }

        var name = entry.GetString("name") ?? deviceId.ToString();
        var distinguishedName = entry.GetString("distinguishedName")
            ?? entry.DistinguishedName;
        device = new DirectoryDevice(deviceId, name, distinguishedName);
        return true;
    }

    /// <summary>Reads a device GUID from its 16-byte LDAP representation.</summary>
    /// <param name="bytes">The raw <c>objectGUID</c> value.</param>
    /// <param name="value">The GUID, or <see cref="Guid.Empty"/> when the value is invalid.</param>
    /// <returns><see langword="true"/> when exactly 16 bytes were supplied.</returns>
    internal static bool TryReadGuid(byte[]? bytes, out Guid value)
    {
        value = Guid.Empty;
        if (bytes is null || bytes.Length != 16)
        {
            return false;
        }

        value = new Guid(bytes);
        return true;
    }

    /// <summary>Pairs a domain DNS endpoint with its complete LDAP search base.</summary>
    /// <param name="Server">The AD DNS domain whose domain controllers are contacted.</param>
    /// <param name="BaseDn">The complete search base distinguished name.</param>
    private sealed record SearchTarget(string Server, string BaseDn);

    /// <summary>Represents one domain advertised by an AD forest cross-reference.</summary>
    /// <param name="DnsRoot">The domain DNS name from <c>dnsRoot</c>.</param>
    /// <param name="NamingContext">The domain naming context from <c>nCName</c>.</param>
    private sealed record ForestDomain(string DnsRoot, string NamingContext);
}