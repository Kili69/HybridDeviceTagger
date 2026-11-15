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

namespace HybridDeviceTagger.Services;

/// <summary>Selects the depth of an LDAP search.</summary>
internal enum ActiveDirectoryLdapSearchScope
{
    /// <summary>Searches only the base object.</summary>
    Base,

    /// <summary>Searches the immediate children of the base object.</summary>
    OneLevel,

    /// <summary>Searches the base object and its entire subtree.</summary>
    Subtree
}

/// <summary>Represents one LDAP search result independent of the underlying LDAP client.</summary>
/// <param name="DistinguishedName">The distinguished name of the entry.</param>
/// <param name="StringAttributes">The requested text attribute values keyed case-insensitively by name.</param>
/// <param name="BinaryAttributes">The requested binary attribute values keyed case-insensitively by name.</param>
internal sealed record ActiveDirectoryLdapEntry(
    string DistinguishedName,
    IReadOnlyDictionary<string, IReadOnlyList<string>> StringAttributes,
    IReadOnlyDictionary<string, IReadOnlyList<byte[]>> BinaryAttributes)
{
    /// <summary>Gets the first text value of an attribute.</summary>
    /// <param name="name">The LDAP attribute name.</param>
    /// <returns>The first value, or <see langword="null"/> when the attribute is absent.</returns>
    public string? GetString(string name) =>
        StringAttributes.TryGetValue(name, out var values) ? values.FirstOrDefault() : null;

    /// <summary>Gets the first binary value of an attribute.</summary>
    /// <param name="name">The LDAP attribute name.</param>
    /// <returns>The first value, or <see langword="null"/> when the attribute is absent.</returns>
    public byte[]? GetBytes(string name) =>
        BinaryAttributes.TryGetValue(name, out var values) ? values.FirstOrDefault() : null;
}

/// <summary>Abstracts an authenticated LDAP v3 connection so platform-specific clients are interchangeable.</summary>
internal interface IActiveDirectoryLdapConnection : IDisposable
{
    /// <summary>Searches the directory and returns all matching entries.</summary>
    /// <param name="baseDn">The search base distinguished name; empty for the RootDSE.</param>
    /// <param name="filter">The RFC 4515 LDAP filter.</param>
    /// <param name="scope">The search depth.</param>
    /// <param name="attributes">The attributes to return.</param>
    /// <param name="pageSize">The simple paged-results page size, or <see langword="null"/> for an unpaged search.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>All matching entries across every result page.</returns>
    /// <exception cref="ActiveDirectoryLdapException">The LDAP search failed.</exception>
    Task<IReadOnlyList<ActiveDirectoryLdapEntry>> SearchAsync(
        string baseDn,
        string filter,
        ActiveDirectoryLdapSearchScope scope,
        IReadOnlyList<string> attributes,
        int? pageSize,
        CancellationToken cancellationToken);

    /// <summary>Changes a password with one modify request that deletes the old and adds the new <c>unicodePwd</c> value.</summary>
    /// <param name="distinguishedName">The distinguished name of the account.</param>
    /// <param name="oldPassword">The quoted UTF-16LE encoding of the current password.</param>
    /// <param name="newPassword">The quoted UTF-16LE encoding of the new password.</param>
    /// <param name="cancellationToken">Cancels the operation before it is sent.</param>
    /// <returns>A task that completes when Active Directory accepted the change.</returns>
    /// <exception cref="ActiveDirectoryLdapException">The LDAP modify request failed.</exception>
    Task ChangePasswordAsync(
        string distinguishedName,
        byte[] oldPassword,
        byte[] newPassword,
        CancellationToken cancellationToken);
}

/// <summary>Represents a failed LDAP operation together with its LDAP result code.</summary>
internal sealed class ActiveDirectoryLdapException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ActiveDirectoryLdapException"/> class.</summary>
    /// <param name="message">The error message, which must not contain credentials.</param>
    /// <param name="resultCode">The LDAP result code, or -1 when unknown.</param>
    /// <param name="innerException">The client-specific exception.</param>
    public ActiveDirectoryLdapException(
        string message,
        int resultCode,
        Exception innerException)
        : base(message, innerException)
    {
        ResultCode = resultCode;
    }

    /// <summary>Gets the RFC 4511 LDAP result code, such as 32 (noSuchObject) or 49 (invalidCredentials).</summary>
    public int ResultCode { get; }
}
