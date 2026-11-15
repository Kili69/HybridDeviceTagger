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

using Novell.Directory.Ldap;

namespace HybridDeviceTagger.Services;

/// <summary>Adapts the managed Novell LDAP client, used for TLS-protected LDAP outside Windows.</summary>
internal sealed class NovellActiveDirectoryLdapConnection : IActiveDirectoryLdapConnection
{
    /// <summary>The bound Novell connection owned by this adapter.</summary>
    private readonly LdapConnection _connection;

    /// <summary>Initializes a new instance of the <see cref="NovellActiveDirectoryLdapConnection"/> class.</summary>
    /// <param name="connection">The bound connection; ownership transfers to this adapter.</param>
    public NovellActiveDirectoryLdapConnection(LdapConnection connection)
    {
        _connection = connection;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActiveDirectoryLdapEntry>> SearchAsync(
        string baseDn,
        string filter,
        ActiveDirectoryLdapSearchScope scope,
        IReadOnlyList<string> attributes,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        try
        {
            var options = new SearchOptions(
                baseDn,
                ToNovellScope(scope),
                filter,
                attributes.ToArray());
            var entries = pageSize.HasValue
                ? await _connection.SearchUsingSimplePagingAsync(
                    options,
                    pageSize.Value,
                    cancellationToken)
                : await ReadAllAsync(options, cancellationToken);
            return entries.Select(entry => ConvertEntry(entry, attributes)).ToArray();
        }
        catch (LdapException exception)
        {
            throw Wrap(exception);
        }
    }

    /// <inheritdoc />
    public async Task ChangePasswordAsync(
        string distinguishedName,
        byte[] oldPassword,
        byte[] newPassword,
        CancellationToken cancellationToken)
    {
        try
        {
            await _connection.ModifyAsync(
                distinguishedName,
                [
                    new LdapModification(
                        LdapModification.Delete,
                        new LdapAttribute("unicodePwd", oldPassword)),
                    new LdapModification(
                        LdapModification.Add,
                        new LdapAttribute("unicodePwd", newPassword))
                ],
                cancellationToken);
        }
        catch (LdapException exception)
        {
            throw Wrap(exception);
        }
    }

    /// <summary>Disposes the underlying Novell connection.</summary>
    public void Dispose() => _connection.Dispose();

    /// <summary>Runs an unpaged search and reads every result.</summary>
    /// <param name="options">The search base, scope, filter, and attributes.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>All returned entries.</returns>
    private async Task<List<LdapEntry>> ReadAllAsync(
        SearchOptions options,
        CancellationToken cancellationToken)
    {
        var results = await _connection.SearchAsync(
            options.SearchBase,
            options.Scope,
            options.Filter,
            options.TargetAttributes,
            typesOnly: false,
            cancellationToken);
        var entries = new List<LdapEntry>();
        while (await results.HasMoreAsync(cancellationToken))
        {
            entries.Add(await results.NextAsync(cancellationToken));
        }
        return entries;
    }

    /// <summary>Converts a Novell entry, keeping only the requested attributes.</summary>
    /// <remarks>
    /// The device-ID attribute (<c>objectGUID</c>) is read as bytes because its value is binary;
    /// every other attribute is read as text.
    /// </remarks>
    /// <param name="entry">The Novell search result.</param>
    /// <param name="requestedAttributes">The attributes requested by the search.</param>
    /// <returns>The client-independent entry.</returns>
    private static ActiveDirectoryLdapEntry ConvertEntry(
        LdapEntry entry,
        IReadOnlyList<string> requestedAttributes)
    {
        var strings = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase);
        var binary = new Dictionary<string, IReadOnlyList<byte[]>>(
            StringComparer.OrdinalIgnoreCase);
        var attributeSet = entry.GetAttributeSet();

        foreach (var name in requestedAttributes)
        {
            if (!attributeSet.TryGetValue(name, out var attribute))
            {
                continue;
            }

            if (string.Equals(name, LdapActiveDirectoryDeviceSource.DeviceIdAttribute,
                StringComparison.OrdinalIgnoreCase))
            {
                binary[name] = attribute.ByteValueArray;
            }
            else
            {
                strings[name] = attribute.StringValueArray;
            }
        }

        return new ActiveDirectoryLdapEntry(entry.Dn, strings, binary);
    }

    /// <summary>Maps the client-independent search scope to the Novell scope constant.</summary>
    /// <param name="scope">The search scope.</param>
    /// <returns>The Novell scope constant.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The scope is not defined.</exception>
    private static int ToNovellScope(ActiveDirectoryLdapSearchScope scope) =>
        scope switch
        {
            ActiveDirectoryLdapSearchScope.Base => LdapConnection.ScopeBase,
            ActiveDirectoryLdapSearchScope.OneLevel => LdapConnection.ScopeOne,
            ActiveDirectoryLdapSearchScope.Subtree => LdapConnection.ScopeSub,
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };

    /// <summary>Wraps a Novell LDAP failure while preserving its result code.</summary>
    /// <param name="exception">The Novell LDAP exception.</param>
    /// <returns>The client-independent exception to throw.</returns>
    private static ActiveDirectoryLdapException Wrap(LdapException exception) =>
        new("The Active Directory LDAP operation failed.", exception.ResultCode, exception);
}
