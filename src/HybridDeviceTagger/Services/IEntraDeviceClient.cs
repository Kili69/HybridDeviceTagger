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

/// <summary>Reads and updates device objects through Microsoft Graph.</summary>
public interface IEntraDeviceClient
{
    /// <summary>Finds an Entra device by its directory device identifier.</summary>
    /// <param name="deviceId">The shared device identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The matching device, or <see langword="null"/> when none exists.</returns>
    Task<EntraDevice?> FindByDeviceIdAsync(Guid deviceId, CancellationToken cancellationToken);

    /// <summary>Sets one or more extension attributes on an Entra device.</summary>
    /// <param name="entraObjectId">The Entra object identifier.</param>
    /// <param name="extensionAttributes">
    /// The Graph attribute names and values to write; a <see langword="null"/> value clears the attribute.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task representing the asynchronous update.</returns>
    Task SetExtensionAttributesAsync(
        string entraObjectId,
        IReadOnlyDictionary<string, string?> extensionAttributes,
        CancellationToken cancellationToken);
}