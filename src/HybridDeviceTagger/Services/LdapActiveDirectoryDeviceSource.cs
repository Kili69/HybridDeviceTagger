/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using System.DirectoryServices.Protocols;
using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Services;

/// <summary>Discovers hybrid-joined computers through paged LDAP searches.</summary>
public sealed class LdapActiveDirectoryDeviceSource : IActiveDirectoryDeviceSource
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
        var isForestRelative = ActiveDirectorySearchPath.IsForestRelative(baseDn);
        var searchTargets = isForestRelative
            ? await GetForestSearchTargetsAsync(baseDn, cancellationToken)
            :
            [
                new SearchTarget(
                    GetLdapServer(ActiveDirectorySearchPath.GetDomainDnsName(baseDn)),
                    baseDn)
            ];
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

    /// <summary>Resolves an OU-only path against every domain advertised by the forest.</summary>
    /// <remarks>
    /// The method reads domain <c>crossRef</c> objects from the configuration partition and pairs
    /// each domain DNS root with the complete domain-specific OU distinguished name.
    /// </remarks>
    private async Task<IReadOnlyList<SearchTarget>> GetForestSearchTargetsAsync(
        string ouPath,
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

        return domainsResponse.Entries
            .Cast<SearchResultEntry>()
            .Select(entry => new
            {
                NamingContext = ReadString(entry.Attributes["nCName"]),
                DnsRoot = ReadString(entry.Attributes["dnsRoot"])
            })
            .Where(domain =>
                !string.IsNullOrWhiteSpace(domain.NamingContext) &&
                !string.IsNullOrWhiteSpace(domain.DnsRoot))
            .Select(domain => new SearchTarget(
                GetLdapServer(domain.DnsRoot!),
                ActiveDirectorySearchPath.CombineWithDomain(ouPath, domain.NamingContext!)))
            .OrderBy(target => target.Server, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
}