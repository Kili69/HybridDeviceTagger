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

namespace HybridDeviceTagger.Models;

/// <summary>Records Entra state owned by HybridDeviceTagger.</summary>
/// <remarks>
/// The ledger only tracks memberships and attributes this application added, so reconciliation can
/// remove them later without touching memberships or values that administrators manage manually.
/// </remarks>
/// <param name="EntraObjectId">The Entra object identifier of the device.</param>
/// <param name="EntraGroupIds">The Entra security groups to which this application added the device.</param>
/// <param name="AdministrativeUnitIds">The administrative units to which this application added the device.</param>
public sealed record ManagedDeviceMemberships(
    string EntraObjectId,
    HashSet<Guid> EntraGroupIds,
    HashSet<Guid> AdministrativeUnitIds)
{
    /// <summary>Gets extension attributes and values managed by HybridDeviceTagger.</summary>
    public Dictionary<string, string> ExtensionAttributes { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates an empty ownership record for an Entra device object.</summary>
    /// <param name="entraObjectId">The Entra object identifier of the device.</param>
    /// <returns>A record without owned memberships or extension attributes.</returns>
    public static ManagedDeviceMemberships Empty(string entraObjectId) =>
        new(entraObjectId, [], []);
}