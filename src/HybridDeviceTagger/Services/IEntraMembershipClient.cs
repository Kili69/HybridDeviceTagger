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

/// <summary>Reads and changes direct Entra device memberships through Microsoft Graph.</summary>
public interface IEntraMembershipClient
{
    /// <summary>Determines whether a device is a direct member of an Entra group.</summary>
    /// <param name="groupId">The Entra security-group object identifier.</param>
    /// <param name="entraObjectId">The Entra device object identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> when the device is a direct member.</returns>
    Task<bool> IsGroupMemberAsync(
        Guid groupId,
        string entraObjectId,
        CancellationToken cancellationToken);

    /// <summary>Adds a device as a direct member of an Entra group.</summary>
    /// <param name="groupId">The Entra security-group object identifier.</param>
    /// <param name="entraObjectId">The Entra device object identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task representing the asynchronous change.</returns>
    Task AddGroupMemberAsync(
        Guid groupId,
        string entraObjectId,
        CancellationToken cancellationToken);

    /// <summary>Removes a device's direct membership from an Entra group.</summary>
    /// <param name="groupId">The Entra security-group object identifier.</param>
    /// <param name="entraObjectId">The Entra device object identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task representing the asynchronous change.</returns>
    Task RemoveGroupMemberAsync(
        Guid groupId,
        string entraObjectId,
        CancellationToken cancellationToken);

    /// <summary>Determines whether a device is a direct member of an administrative unit.</summary>
    /// <param name="administrativeUnitId">The administrative-unit object identifier.</param>
    /// <param name="entraObjectId">The Entra device object identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> when the device is a direct member.</returns>
    Task<bool> IsAdministrativeUnitMemberAsync(
        Guid administrativeUnitId,
        string entraObjectId,
        CancellationToken cancellationToken);

    /// <summary>Adds a device to a normal or restricted administrative unit.</summary>
    /// <param name="administrativeUnitId">The administrative-unit object identifier.</param>
    /// <param name="entraObjectId">The Entra device object identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task representing the asynchronous change.</returns>
    Task AddAdministrativeUnitMemberAsync(
        Guid administrativeUnitId,
        string entraObjectId,
        CancellationToken cancellationToken);

    /// <summary>Removes a device from a normal or restricted administrative unit.</summary>
    /// <param name="administrativeUnitId">The administrative-unit object identifier.</param>
    /// <param name="entraObjectId">The Entra device object identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task representing the asynchronous change.</returns>
    Task RemoveAdministrativeUnitMemberAsync(
        Guid administrativeUnitId,
        string entraObjectId,
        CancellationToken cancellationToken);
}