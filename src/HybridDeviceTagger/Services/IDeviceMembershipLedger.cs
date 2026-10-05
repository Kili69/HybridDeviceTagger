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

/// <summary>Persists Entra state that HybridDeviceTagger is allowed to remove later.</summary>
public interface IDeviceMembershipLedger
{
    /// <summary>Loads all managed device states keyed by hybrid device identifier.</summary>
    Task<IReadOnlyDictionary<Guid, ManagedDeviceMemberships>> GetAllAsync(
        CancellationToken cancellationToken);

    /// <summary>Loads ownership state for a hybrid device identifier.</summary>
    Task<ManagedDeviceMemberships?> GetAsync(
        Guid deviceId,
        CancellationToken cancellationToken);

    /// <summary>Persists or deletes empty ownership state for a hybrid device identifier.</summary>
    Task SetAsync(
        Guid deviceId,
        ManagedDeviceMemberships memberships,
        CancellationToken cancellationToken);
}