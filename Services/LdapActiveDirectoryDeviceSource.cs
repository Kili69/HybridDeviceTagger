using System.DirectoryServices.Protocols;
using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Services;

public sealed class LdapActiveDirectoryDeviceSource : IActiveDirectoryDeviceSource
{
    private const string HybridDeviceFilter = "(&(objectCategory=computer)(msDS-DeviceID=*))";
    private readonly ActiveDirectoryOptions _options;
    private readonly IActiveDirectoryCredentialStore _credentialStore;
    private readonly ActiveDirectoryLdapConnectionFactory _connectionFactory;

    public LdapActiveDirectoryDeviceSource(
        IOptions<ActiveDirectoryOptions> options,
        IActiveDirectoryCredentialStore credentialStore,
        ActiveDirectoryLdapConnectionFactory connectionFactory)
    {
        _options = options.Value;
        _credentialStore = credentialStore;
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<DirectoryDevice>> GetHybridJoinedDevicesAsync(
        CancellationToken cancellationToken)
    {
        using var connection = await CreateConnectionAsync(cancellationToken);
        var devices = new List<DirectoryDevice>();
        var pageRequest = new PageResultRequestControl(_options.PageSize);

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            var request = new SearchRequest(
                _options.BaseDn,
                HybridDeviceFilter,
                SearchScope.Subtree,
                "msDS-DeviceID",
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

    private async Task<LdapConnection> CreateConnectionAsync(
        CancellationToken cancellationToken)
    {
        var current = await _credentialStore.GetCurrentAsync(cancellationToken);
        try
        {
            return _connectionFactory.CreateBoundConnection(current);
        }
        catch (LdapException)
        {
            var next = await _credentialStore.GetNextAsync(cancellationToken);
            if (next is null)
            {
                throw;
            }

            return _connectionFactory.CreateBoundConnection(next);
        }
    }

    private static bool TryReadDevice(SearchResultEntry entry, out DirectoryDevice device)
    {
        device = default!;
        if (!TryReadGuid(entry.Attributes["msDS-DeviceID"], out var deviceId))
        {
            return false;
        }

        var name = ReadString(entry.Attributes["name"]) ?? deviceId.ToString();
        var distinguishedName = ReadString(entry.Attributes["distinguishedName"])
            ?? entry.DistinguishedName;
        device = new DirectoryDevice(deviceId, name, distinguishedName);
        return true;
    }

    private static bool TryReadGuid(DirectoryAttribute? attribute, out Guid value)
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

    private static bool TryCreateGuid(byte[] bytes, out Guid value)
    {
        value = new Guid(bytes);
        return true;
    }

    private static string? ReadString(DirectoryAttribute? attribute) =>
        attribute is null || attribute.Count == 0
            ? null
            : attribute[0]?.ToString();
}