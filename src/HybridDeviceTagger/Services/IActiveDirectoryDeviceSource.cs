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

/// <summary>Discovers hybrid-joined computer objects in Active Directory.</summary>
public interface IActiveDirectoryDeviceSource
{
    /// <summary>Gets hybrid-joined computers in and below matching Active Directory OUs.</summary>
    /// <param name="baseDn">
    /// A domain-specific distinguished name or an OU-only path resolved in every forest domain.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The discovered directory devices.</returns>
    Task<IReadOnlyList<DirectoryDevice>> GetHybridJoinedDevicesAsync(
        string baseDn,
        CancellationToken cancellationToken);
}