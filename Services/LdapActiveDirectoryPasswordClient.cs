using System.DirectoryServices.Protocols;
using System.Text;
using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Services;

public sealed class LdapActiveDirectoryPasswordClient : IActiveDirectoryPasswordClient
{
    private readonly ActiveDirectoryLdapConnectionFactory _connectionFactory;
    private readonly ActiveDirectoryOptions _options;

    public LdapActiveDirectoryPasswordClient(
        ActiveDirectoryLdapConnectionFactory connectionFactory,
        IOptions<ActiveDirectoryOptions> options)
    {
        _connectionFactory = connectionFactory;
        _options = options.Value;
    }

    public Task<bool> CanBindAsync(
        StoredDirectoryCredential credential,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var connection = _connectionFactory.CreateBoundConnection(credential);
            return Task.FromResult(true);
        }
        catch (LdapException)
        {
            return Task.FromResult(false);
        }
    }

    public Task ChangePasswordAsync(
        StoredDirectoryCredential currentCredential,
        string newPassword,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = _connectionFactory.CreateBoundConnection(currentCredential);

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