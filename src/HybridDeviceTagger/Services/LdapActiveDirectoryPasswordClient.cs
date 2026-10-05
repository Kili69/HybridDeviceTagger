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

    /// <summary>Tests a credential through either the normal bind path or mandatory LDAPS.</summary>
    /// <remarks>
    /// LDAP failures become <see langword="false"/> because failed credentials are an expected
    /// input to rotation recovery. Cancellation and non-LDAP failures still propagate.
    /// </remarks>
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

    /// <summary>Encodes the quoted UTF-16LE representation required by AD's <c>unicodePwd</c>.</summary>
    /// <remarks>Omitting the surrounding quotes causes Active Directory to reject the syntax.</remarks>
    private static byte[] ToUnicodePassword(string password) =>
        Encoding.Unicode.GetBytes($"\"{password}\"");
}