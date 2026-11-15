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

/// <summary>Adapts System.DirectoryServices.Protocols, used on Windows.</summary>
/// <remarks>The underlying client is synchronous; cancellation is honored between result pages.</remarks>
internal sealed class SystemDirectoryServicesLdapConnection : IActiveDirectoryLdapConnection
{
    /// <summary>The bound connection owned by this adapter.</summary>
    private readonly LdapConnection _connection;

    /// <summary>Initializes a new instance of the <see cref="SystemDirectoryServicesLdapConnection"/> class.</summary>
    /// <param name="connection">The bound connection; ownership transfers to this adapter.</param>
    public SystemDirectoryServicesLdapConnection(LdapConnection connection)
    {
        _connection = connection;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <summary>Disposes the underlying LDAP connection.</summary>
    public void Dispose() => _connection.Dispose();

    /// <summary>Converts a search result, keeping only the requested attributes.</summary>
    /// <remarks>Values are classified as binary or text by the runtime type returned by the client.</remarks>
    /// <param name="entry">The search result entry.</param>
    /// <param name="requestedAttributes">The attributes requested by the search.</param>
    /// <returns>The client-independent entry.</returns>
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

    /// <summary>Maps the client-independent search scope to the System.DirectoryServices.Protocols scope.</summary>
    /// <param name="scope">The search scope.</param>
    /// <returns>The equivalent <see cref="SearchScope"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The scope is not defined.</exception>
    private static SearchScope ToSystemScope(ActiveDirectoryLdapSearchScope scope) =>
        scope switch
        {
            ActiveDirectoryLdapSearchScope.Base => SearchScope.Base,
            ActiveDirectoryLdapSearchScope.OneLevel => SearchScope.OneLevel,
            ActiveDirectoryLdapSearchScope.Subtree => SearchScope.Subtree,
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };

    /// <summary>Wraps an LDAP failure with its result code.</summary>
    /// <param name="exception">The client exception.</param>
    /// <param name="resultCode">The LDAP result code, or -1 when the response carried none.</param>
    /// <returns>The client-independent exception to throw.</returns>
    private static ActiveDirectoryLdapException Wrap(Exception exception, int resultCode) =>
        new("The Active Directory LDAP operation failed.", resultCode, exception);
}
