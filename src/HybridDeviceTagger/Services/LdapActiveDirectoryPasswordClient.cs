using System.DirectoryServices.Protocols;
using System.Text;
using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Services;

/// <summary>Validates and changes the service-account password through LDAPS.</summary>
public sealed class LdapActiveDirectoryPasswordClient : IActiveDirectoryPasswordClient
{
    private readonly ActiveDirectoryLdapConnectionFactory _connectionFactory;
    private readonly ActiveDirectoryOptions _options;

    /// <summary>Initializes a new instance of the <see cref="LdapActiveDirectoryPasswordClient"/> class.</summary>
    /// <param name="connectionFactory">The authenticated LDAP connection factory.</param>
    /// <param name="options">The service-account directory settings.</param>
    public LdapActiveDirectoryPasswordClient(
        ActiveDirectoryLdapConnectionFactory connectionFactory,
        IOptions<ActiveDirectoryOptions> options)
    {
        _connectionFactory = connectionFactory;
        _options = options.Value;
    }

    /// <inheritdoc />
    public Task<bool> CanBindAsync(
        StoredDirectoryCredential credential,
        CancellationToken cancellationToken)
    {
        return CanBindAsync(
            credential,
            requireSecureConnection: false,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CanBindSecurelyAsync(
        StoredDirectoryCredential credential,
        CancellationToken cancellationToken)
    {
        return CanBindAsync(
            credential,
            requireSecureConnection: true,
            cancellationToken);
    }

    private Task<bool> CanBindAsync(
        StoredDirectoryCredential credential,
        bool requireSecureConnection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var connection = requireSecureConnection
                ? _connectionFactory.CreateSecureBoundConnection(credential)
                : _connectionFactory.CreateBoundConnection(credential);
            return Task.FromResult(true);
        }
        catch (LdapException)
        {
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Active Directory receives one atomic modify request that deletes the quoted UTF-16 old
    /// password and adds the quoted UTF-16 new password.
    /// </remarks>
    public Task ChangePasswordAsync(
        StoredDirectoryCredential currentCredential,
        string newPassword,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = _connectionFactory.CreateSecureBoundConnection(currentCredential);

        var deleteOldPassword = new DirectoryAttributeModification
        {
            Name = "unicodePwd",
            Operation = DirectoryAttributeOperation.Delete
        };
        deleteOldPassword.Add(ToUnicodePassword(currentCredential.Password));

        var addNewPassword = new DirectoryAttributeModification
        {
            Name = "unicodePwd",
            Operation = DirectoryAttributeOperation.Add
        };
        addNewPassword.Add(ToUnicodePassword(newPassword));

        var request = new ModifyRequest(
            _options.BindUserDistinguishedName,
            deleteOldPassword,
            addNewPassword);
        connection.SendRequest(request);
        return Task.CompletedTask;
    }

    private static byte[] ToUnicodePassword(string password) =>
        Encoding.Unicode.GetBytes($"\"{password}\"");
}