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

internal sealed class NovellActiveDirectoryLdapConnection : IActiveDirectoryLdapConnection
{
    private readonly LdapConnection _connection;

    public NovellActiveDirectoryLdapConnection(LdapConnection connection)
    {
        _connection = connection;
    }

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

    public void Dispose() => _connection.Dispose();

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

    private static int ToNovellScope(ActiveDirectoryLdapSearchScope scope) =>
        scope switch
        {
            ActiveDirectoryLdapSearchScope.Base => LdapConnection.ScopeBase,
            ActiveDirectoryLdapSearchScope.OneLevel => LdapConnection.ScopeOne,
            ActiveDirectoryLdapSearchScope.Subtree => LdapConnection.ScopeSub,
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };

    private static ActiveDirectoryLdapException Wrap(LdapException exception) =>
        new("The Active Directory LDAP operation failed.", exception.ResultCode, exception);
}
