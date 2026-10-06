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
    internal const string ComputerFilter =
        "(&(objectCategory=computer)(userCertificate=*))";
    internal const string DeviceIdAttribute = "objectGUID";
    private readonly ActiveDirectoryOptions _options;
    private readonly IActiveDirectoryCredentialStore _credentialStore;
    private readonly ActiveDirectoryLdapConnectionFactory _connectionFactory;
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
    /// every domain. Queries use server-side paging, accept binary and string GUID values, and
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
                foreach (var device in SearchDevices(connection, target.BaseDn, cancellationToken))
                {
                    devices[device.DeviceId] = device;
                }
                resolvedOuCount++;
            }
            catch (DirectoryOperationException exception) when (
                isForestRelative && exception.Response?.ResultCode == ResultCode.NoSuchObject)
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
    private IReadOnlyList<DirectoryDevice> SearchDevices(
        LdapConnection connection,
        string baseDn,
        CancellationToken cancellationToken)
    {
        var devices = new List<DirectoryDevice>();
        var pageRequest = new PageResultRequestControl(_options.PageSize);

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            var request = new SearchRequest(
                baseDn,
                ComputerFilter,
                SearchScope.Subtree,
                DeviceIdAttribute,
                "name",
                "distinguishedName");
            request.Controls.Add(pageRequest);

            var response = (SearchResponse)connection.SendRequest(request);
            foreach (SearchResultEntry entry in response.Entries)
            {
                if (TryReadDevice(entry, out var device))
                {
                    devices.Add(device);
                }
            }

            pageRequest.Cookie = response.Controls
                .OfType<PageResultResponseControl>()
                .Single()
                .Cookie;
        }
        while (pageRequest.Cookie.Length > 0);

        return devices;
    }

    /// <summary>Reads every domain advertised by the configured forest.</summary>
    /// <remarks>
    /// Discovery always starts through the configured server, never through a server name derived
    /// from a rule. Domain <c>crossRef</c> objects are read from the configuration partition.
    /// </remarks>
    private async Task<IReadOnlyList<ForestDomain>> GetForestDomainsAsync(
        CancellationToken cancellationToken)
    {
        using var connection = await CreateConnectionAsync(_options.Server, cancellationToken);
        var rootResponse = (SearchResponse)connection.SendRequest(new SearchRequest(
            string.Empty,
            "(objectClass=*)",
            SearchScope.Base,
            "configurationNamingContext"));
        var configurationNamingContext = ReadString(
            rootResponse.Entries[0].Attributes["configurationNamingContext"])
            ?? throw new InvalidOperationException(
                "Active Directory did not return a configuration naming context.");
        var partitionsDn = $"CN=Partitions,{configurationNamingContext}";
        var domainsResponse = (SearchResponse)connection.SendRequest(new SearchRequest(
            partitionsDn,
            "(&(objectClass=crossRef)(systemFlags:1.2.840.113556.1.4.803:=3))",
            SearchScope.OneLevel,
            "nCName",
            "dnsRoot"));

        var domains = domainsResponse.Entries
            .Cast<SearchResultEntry>()
            .Select(entry => new
            {
                NamingContext = ReadString(entry.Attributes["nCName"]),
                DnsRoot = ReadString(entry.Attributes["dnsRoot"])
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
    private IReadOnlyList<SearchTarget> GetForestSearchTargets(
        string ouPath,
        IReadOnlyList<ForestDomain> forestDomains) =>
        forestDomains
            .Select(domain => new SearchTarget(
                GetLdapServer(domain.DnsRoot),
                ActiveDirectorySearchPath.CombineWithDomain(ouPath, domain.NamingContext)))
            .ToArray();

    /// <summary>Resolves a domain-specific path only when its domain belongs to the forest.</summary>
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
    internal static bool IsForestDomain(
        string baseDn,
        IEnumerable<string> forestDomainDnsRoots)
    {
        var domainDnsName = ActiveDirectorySearchPath.GetDomainDnsName(baseDn);
        return forestDomainDnsRoots.Any(dnsRoot =>
            string.Equals(dnsRoot, domainDnsName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Uses the configured DC when it belongs to the domain being searched.</summary>
    private string GetLdapServer(string domainDnsName) =>
        _options.Server.EndsWith($".{domainDnsName}", StringComparison.OrdinalIgnoreCase)
            ? _options.Server
            : domainDnsName;

    /// <summary>Creates a connection with automatic staged-credential recovery.</summary>
    /// <remarks>
    /// The active credential is tried first. If its bind fails during an interrupted rotation,
    /// the staged <c>next</c> credential is loaded and tried against the same server.
    /// </remarks>
    private async Task<LdapConnection> CreateConnectionAsync(
        string server,
        CancellationToken cancellationToken)
    {
        var current = await _credentialStore.GetCurrentAsync(cancellationToken);
        try
        {
            return _connectionFactory.CreateBoundConnection(current, server);
        }
        catch (LdapException)
        {
            var next = await _credentialStore.GetNextAsync(cancellationToken);
            if (next is null)
            {
                throw;
            }

            return _connectionFactory.CreateBoundConnection(next, server);
        }
    }

    /// <summary>Projects one LDAP entry when it contains a valid hybrid device identifier.</summary>
    private static bool TryReadDevice(SearchResultEntry entry, out DirectoryDevice device)
    {
        device = default!;
        if (!TryReadGuid(entry.Attributes[DeviceIdAttribute], out var deviceId))
        {
            return false;
        }

        var name = ReadString(entry.Attributes["name"]) ?? deviceId.ToString();
        var distinguishedName = ReadString(entry.Attributes["distinguishedName"])
            ?? entry.DistinguishedName;
        device = new DirectoryDevice(deviceId, name, distinguishedName);
        return true;
    }

    /// <summary>Reads a device GUID from either its 16-byte LDAP form or textual form.</summary>
    internal static bool TryReadGuid(DirectoryAttribute? attribute, out Guid value)
    {
        value = Guid.Empty;
        if (attribute is null || attribute.Count == 0)
        {
            return false;
        }

        return attribute[0] switch
        {
            byte[] bytes when bytes.Length == 16 => TryCreateGuid(bytes, out value),
            string text => Guid.TryParse(text.Trim('{', '}'), out value),
            _ => false
        };
    }

    /// <summary>Constructs a GUID from a validated 16-byte value.</summary>
    private static bool TryCreateGuid(byte[] bytes, out Guid value)
    {
        value = new Guid(bytes);
        return true;
    }

    /// <summary>Reads the first LDAP attribute value as text, or returns <see langword="null"/>.</summary>
    private static string? ReadString(DirectoryAttribute? attribute) =>
        attribute is null || attribute.Count == 0
            ? null
            : attribute[0]?.ToString();

    /// <summary>Pairs a domain DNS endpoint with its complete LDAP search base.</summary>
    private sealed record SearchTarget(string Server, string BaseDn);

    /// <summary>Represents one domain advertised by an AD forest cross-reference.</summary>
    private sealed record ForestDomain(string DnsRoot, string NamingContext);
}