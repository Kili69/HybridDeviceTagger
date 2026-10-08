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

internal enum ActiveDirectoryLdapSearchScope
{
    Base,
    OneLevel,
    Subtree
}

internal sealed record ActiveDirectoryLdapEntry(
    string DistinguishedName,
    IReadOnlyDictionary<string, IReadOnlyList<string>> StringAttributes,
    IReadOnlyDictionary<string, IReadOnlyList<byte[]>> BinaryAttributes)
{
    public string? GetString(string name) =>
        StringAttributes.TryGetValue(name, out var values) ? values.FirstOrDefault() : null;

    public byte[]? GetBytes(string name) =>
        BinaryAttributes.TryGetValue(name, out var values) ? values.FirstOrDefault() : null;
}

internal interface IActiveDirectoryLdapConnection : IDisposable
{
    Task<IReadOnlyList<ActiveDirectoryLdapEntry>> SearchAsync(
        string baseDn,
        string filter,
        ActiveDirectoryLdapSearchScope scope,
        IReadOnlyList<string> attributes,
        int? pageSize,
        CancellationToken cancellationToken);

    Task ChangePasswordAsync(
        string distinguishedName,
        byte[] oldPassword,
        byte[] newPassword,
        CancellationToken cancellationToken);
}

internal sealed class ActiveDirectoryLdapException : Exception
{
    public ActiveDirectoryLdapException(
        string message,
        int resultCode,
        Exception innerException)
        : base(message, innerException)
    {
        ResultCode = resultCode;
    }

    public int ResultCode { get; }
}
