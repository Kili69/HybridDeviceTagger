using System.DirectoryServices.Protocols;
using System.Net;
using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Services;

public sealed class ActiveDirectoryLdapConnectionFactory
{
    private readonly ActiveDirectoryOptions _options;

    public ActiveDirectoryLdapConnectionFactory(IOptions<ActiveDirectoryOptions> options)
    {
        _options = options.Value;
    }

    public LdapConnection CreateBoundConnection(StoredDirectoryCredential credential)
    {
        var identifier = new LdapDirectoryIdentifier(_options.Server, _options.Port);
        var networkCredential = new NetworkCredential(credential.Username, credential.Password);
        var connection = new LdapConnection(identifier, networkCredential, AuthType.Basic)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.SecureSocketLayer = _options.UseSsl;

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
}