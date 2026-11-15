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

using HybridDeviceTagger.Models;

namespace HybridDeviceTagger.Services;

/// <summary>Validates and changes the configured Active Directory service-account password.</summary>
public interface IActiveDirectoryPasswordClient
{
    /// <summary>Tests whether a credential can complete an LDAP bind.</summary>
    /// <param name="credential">The credential to test.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> when the bind succeeds; otherwise <see langword="false"/>.</returns>
    Task<bool> CanBindAsync(
        StoredDirectoryCredential credential,
        CancellationToken cancellationToken);

    /// <summary>Tests whether a credential can complete an LDAPS bind without fallback.</summary>
    /// <param name="credential">The credential to test.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> when the secure bind succeeds; otherwise <see langword="false"/>.</returns>
    Task<bool> CanBindSecurelyAsync(
        StoredDirectoryCredential credential,
        CancellationToken cancellationToken);

    /// <summary>Atomically changes the service-account password over LDAPS.</summary>
    /// <param name="currentCredential">The credential currently accepted by AD.</param>
    /// <param name="newPassword">The replacement password.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task representing the asynchronous password change.</returns>
    Task ChangePasswordAsync(
        StoredDirectoryCredential currentCredential,
        string newPassword,
        CancellationToken cancellationToken);
}