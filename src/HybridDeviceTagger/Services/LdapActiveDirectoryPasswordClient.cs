/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using System.DirectoryServices.Protocols;
using System.Text;
using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Services;

/// <summary>Validates and changes the service-account password through LDAPS.</summary>
public sealed class LdapActiveDirectoryPasswordClient : IActiveDirectoryPasswordClient
{
    /// <summary>Creates bound LDAP connections for bind tests and the password change.</summary>
    private readonly ActiveDirectoryLdapConnectionFactory _connectionFactory;

    /// <summary>Supplies the distinguished name of the account whose password is changed.</summary>
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

    /// <summary>Tests a credential through either the normal bind path or mandatory LDAPS.</summary>
    /// <remarks>
    /// LDAP failures become <see langword="false"/> because failed credentials are an expected
    /// input to rotation recovery. Cancellation and non-LDAP failures still propagate.
    /// </remarks>
    /// <param name="credential">The credential to test.</param>
    /// <param name="requireSecureConnection">
    /// <see langword="true"/> to require LDAPS; otherwise the configured LDAP fallback may be used.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> when the bind succeeds.</returns>
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
    /// password and adds the quoted UTF-16 new password. This is a user password change rather than
    /// an administrative reset, so the account needs no reset permission and AD password policy
    /// applies. The connection always uses LDAPS because AD rejects <c>unicodePwd</c> changes
    /// over unencrypted LDAP.
    /// </remarks>
    /// <exception cref="LdapException">The secure bind failed.</exception>
    /// <exception cref="DirectoryOperationException">Active Directory rejected the password change.</exception>
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

    /// <summary>Encodes the quoted UTF-16LE representation required by AD's <c>unicodePwd</c>.</summary>
    /// <remarks>Omitting the surrounding quotes causes Active Directory to reject the syntax.</remarks>
    /// <param name="password">The plain-text password.</param>
    /// <returns>The encoded attribute value.</returns>
    private static byte[] ToUnicodePassword(string password) =>
        Encoding.Unicode.GetBytes($"\"{password}\"");
}