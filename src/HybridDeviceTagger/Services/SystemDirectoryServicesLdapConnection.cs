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

using System.Collections;
using System.DirectoryServices.Protocols;

namespace HybridDeviceTagger.Services;

internal sealed class SystemDirectoryServicesLdapConnection : IActiveDirectoryLdapConnection
{
    private readonly LdapConnection _connection;

    public SystemDirectoryServicesLdapConnection(LdapConnection connection)
    {
        _connection = connection;
    }

    public Task<IReadOnlyList<ActiveDirectoryLdapEntry>> SearchAsync(
        string baseDn,
        string filter,
        ActiveDirectoryLdapSearchScope scope,
        IReadOnlyList<string> attributes,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var entries = new List<ActiveDirectoryLdapEntry>();
            var pageControl = pageSize.HasValue
                ? new PageResultRequestControl(pageSize.Value)
                : null;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var request = new SearchRequest(
                    baseDn,
                    filter,
                    ToSystemScope(scope),
                    attributes.ToArray());
                if (pageControl is not null)
                {
                    request.Controls.Add(pageControl);
                }

                var response = (SearchResponse)_connection.SendRequest(request);
                entries.AddRange(response.Entries
                    .Cast<SearchResultEntry>()
                    .Select(entry => ConvertEntry(entry, attributes)));

                if (pageControl is null)
                {
                    break;
                }

                pageControl.Cookie = response.Controls
                    .OfType<PageResultResponseControl>()
                    .Single()
                    .Cookie;
            }
            while (pageControl.Cookie.Length > 0);

            return Task.FromResult<IReadOnlyList<ActiveDirectoryLdapEntry>>(entries);
        }
        catch (LdapException exception)
        {
            throw Wrap(exception, exception.ErrorCode);
        }
        catch (DirectoryOperationException exception)
        {
            throw Wrap(exception, (int?)exception.Response?.ResultCode ?? -1);
        }
    }

    public Task ChangePasswordAsync(
        string distinguishedName,
        byte[] oldPassword,
        byte[] newPassword,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deleteOldPassword = new DirectoryAttributeModification
        {
            Name = "unicodePwd",
            Operation = DirectoryAttributeOperation.Delete
        };
        deleteOldPassword.Add(oldPassword);

        var addNewPassword = new DirectoryAttributeModification
        {
            Name = "unicodePwd",
            Operation = DirectoryAttributeOperation.Add
        };
        addNewPassword.Add(newPassword);

        try
        {
            _connection.SendRequest(new ModifyRequest(
                distinguishedName,
                deleteOldPassword,
                addNewPassword));
            return Task.CompletedTask;
        }
        catch (LdapException exception)
        {
            throw Wrap(exception, exception.ErrorCode);
        }
        catch (DirectoryOperationException exception)
        {
            throw Wrap(exception, (int?)exception.Response?.ResultCode ?? -1);
        }
    }

    public void Dispose() => _connection.Dispose();

    private static ActiveDirectoryLdapEntry ConvertEntry(
        SearchResultEntry entry,
        IReadOnlyList<string> requestedAttributes)
    {
        var strings = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase);
        var binary = new Dictionary<string, IReadOnlyList<byte[]>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var name in requestedAttributes)
        {
            var attribute = entry.Attributes[name];
            if (attribute is null)
            {
                continue;
            }

            var stringValues = new List<string>();
            var binaryValues = new List<byte[]>();
            foreach (var value in (IEnumerable)attribute)
            {
                if (value is byte[] bytes)
                {
                    binaryValues.Add(bytes);
                }
                else if (value is not null)
                {
                    stringValues.Add(value.ToString()!);
                }
            }

            if (stringValues.Count > 0)
            {
                strings[name] = stringValues;
            }
            if (binaryValues.Count > 0)
            {
                binary[name] = binaryValues;
            }
        }

        return new ActiveDirectoryLdapEntry(entry.DistinguishedName, strings, binary);
    }

    private static SearchScope ToSystemScope(ActiveDirectoryLdapSearchScope scope) =>
        scope switch
        {
            ActiveDirectoryLdapSearchScope.Base => SearchScope.Base,
            ActiveDirectoryLdapSearchScope.OneLevel => SearchScope.OneLevel,
            ActiveDirectoryLdapSearchScope.Subtree => SearchScope.Subtree,
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };

    private static ActiveDirectoryLdapException Wrap(Exception exception, int resultCode) =>
        new("The Active Directory LDAP operation failed.", resultCode, exception);
}
