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

/// <summary>Reconciles desired Entra memberships without deleting manually managed memberships.</summary>
public sealed class DeviceMembershipReconciliationService
{
    private readonly IEntraMembershipClient _membershipClient;
    private readonly IDeviceMembershipLedger _ledger;

    /// <summary>Initializes a new instance of the reconciliation service.</summary>
    public DeviceMembershipReconciliationService(
        IEntraMembershipClient membershipClient,
        IDeviceMembershipLedger ledger)
    {
        _membershipClient = membershipClient;
        _ledger = ledger;
    }

    /// <summary>Applies desired memberships and removes only memberships recorded in the ledger.</summary>
    /// <param name="deviceId">The device identifier shared by Active Directory and Entra.</param>
    /// <param name="entraObjectId">The Entra directory object identifier.</param>
    /// <param name="desiredGroupIds">The desired Entra security-group identifiers.</param>
    /// <param name="desiredAdministrativeUnitIds">The desired administrative-unit identifiers.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <param name="trace">The optional structured diagnostic trace for this run.</param>
    /// <param name="machineName">The optional Active Directory computer name for trace correlation.</param>
    /// <returns>The number of memberships added and removed.</returns>
    public async Task<MembershipReconciliationResult> ReconcileAsync(
        Guid deviceId,
        string entraObjectId,
        IReadOnlySet<Guid> desiredGroupIds,
        IReadOnlySet<Guid> desiredAdministrativeUnitIds,
        CancellationToken cancellationToken,
        IRunTraceSession? trace = null,
        string? machineName = null)
    {
        trace ??= RunTrace.None;
        var memberships = await _ledger.GetAsync(deviceId, cancellationToken);
        if (memberships is null ||
            !string.Equals(memberships.EntraObjectId, entraObjectId, StringComparison.OrdinalIgnoreCase))
        {
            memberships = ManagedDeviceMemberships.Empty(entraObjectId);
        }

        await trace.WriteAsync(
            "Information",
            "Membership",
            "LoadOwnershipState",
            "Succeeded",
            MembershipDetails(
                deviceId,
                entraObjectId,
                "Ledger",
                null,
                memberships.EntraGroupIds.Count + memberships.AdministrativeUnitIds.Count,
                machineName),
            null,
            cancellationToken);

        var added = 0;
        var removed = 0;
        foreach (var groupId in desiredGroupIds)
        {
            var groupDetails = MembershipDetails(
                deviceId,
                entraObjectId,
                "Group",
                groupId,
                memberships.EntraGroupIds.Contains(groupId) ? 1 : 0,
                machineName);
            await trace.WriteAsync(
                "Information",
                "Membership",
                "CheckMembership",
                "Started",
                groupDetails,
                null,
                cancellationToken);
            var isMember = await _membershipClient.IsGroupMemberAsync(
                groupId,
                entraObjectId,
                cancellationToken);
            await trace.WriteAsync(
                "Information",
                "Membership",
                "CheckMembership",
                isMember ? "Present" : "Missing",
                groupDetails,
                null,
                cancellationToken);
            if (!isMember)
            {
                await _membershipClient.AddGroupMemberAsync(
                    groupId,
                    entraObjectId,
                    cancellationToken);
                memberships.EntraGroupIds.Add(groupId);
                await _ledger.SetAsync(deviceId, memberships, cancellationToken);
                added++;
                await trace.WriteAsync(
                    "Information",
                    "Membership",
                    "AddMembership",
                    "Succeeded",
                    groupDetails,
                    null,
                    cancellationToken);
            }
            else if (!memberships.EntraGroupIds.Contains(groupId))
            {
                await trace.WriteAsync(
                    "Information",
                    "Membership",
                    "PreservePreexistingMembership",
                    "NotClaimed",
                    groupDetails,
                    null,
                    cancellationToken);
            }
        }

        foreach (var groupId in memberships.EntraGroupIds.Except(desiredGroupIds).ToArray())
        {
            var groupDetails = MembershipDetails(deviceId, entraObjectId, "Group", groupId, 1, machineName);
            await trace.WriteAsync(
                "Information",
                "Membership",
                "RemoveOwnedMembership",
                "Started",
                groupDetails,
                null,
                cancellationToken);
            if (await _membershipClient.IsGroupMemberAsync(
                    groupId,
                    entraObjectId,
                    cancellationToken))
            {
                await _membershipClient.RemoveGroupMemberAsync(
                    groupId,
                    entraObjectId,
                    cancellationToken);
                removed++;
            }

            memberships.EntraGroupIds.Remove(groupId);
            await _ledger.SetAsync(deviceId, memberships, cancellationToken);
            await trace.WriteAsync(
                "Information",
                "Membership",
                "RemoveOwnedMembership",
                "Succeeded",
                groupDetails,
                null,
                cancellationToken);
        }

        foreach (var administrativeUnitId in desiredAdministrativeUnitIds)
        {
            var administrativeUnitDetails = MembershipDetails(
                deviceId,
                entraObjectId,
                "AdministrativeUnitOrRestrictedAdministrativeUnit",
                administrativeUnitId,
                memberships.AdministrativeUnitIds.Contains(administrativeUnitId) ? 1 : 0,
                machineName);
            await trace.WriteAsync(
                "Information",
                "Membership",
                "CheckMembership",
                "Started",
                administrativeUnitDetails,
                null,
                cancellationToken);
            var isMember = await _membershipClient.IsAdministrativeUnitMemberAsync(
                administrativeUnitId,
                entraObjectId,
                cancellationToken);
            await trace.WriteAsync(
                "Information",
                "Membership",
                "CheckMembership",
                isMember ? "Present" : "Missing",
                administrativeUnitDetails,
                null,
                cancellationToken);
            if (!isMember)
            {
                await _membershipClient.AddAdministrativeUnitMemberAsync(
                    administrativeUnitId,
                    entraObjectId,
                    cancellationToken);
                memberships.AdministrativeUnitIds.Add(administrativeUnitId);
                await _ledger.SetAsync(deviceId, memberships, cancellationToken);
                added++;
                await trace.WriteAsync(
                    "Information",
                    "Membership",
                    "AddMembership",
                    "Succeeded",
                    administrativeUnitDetails,
                    null,
                    cancellationToken);
            }
            else if (!memberships.AdministrativeUnitIds.Contains(administrativeUnitId))
            {
                await trace.WriteAsync(
                    "Information",
                    "Membership",
                    "PreservePreexistingMembership",
                    "NotClaimed",
                    administrativeUnitDetails,
                    null,
                    cancellationToken);
            }
        }

        foreach (var administrativeUnitId in memberships.AdministrativeUnitIds
                     .Except(desiredAdministrativeUnitIds)
                     .ToArray())
        {
            var administrativeUnitDetails = MembershipDetails(
                deviceId,
                entraObjectId,
                "AdministrativeUnitOrRestrictedAdministrativeUnit",
                administrativeUnitId,
                1,
                machineName);
            await trace.WriteAsync(
                "Information",
                "Membership",
                "RemoveOwnedMembership",
                "Started",
                administrativeUnitDetails,
                null,
                cancellationToken);
            if (await _membershipClient.IsAdministrativeUnitMemberAsync(
                    administrativeUnitId,
                    entraObjectId,
                    cancellationToken))
            {
                await _membershipClient.RemoveAdministrativeUnitMemberAsync(
                    administrativeUnitId,
                    entraObjectId,
                    cancellationToken);
                removed++;
            }

            memberships.AdministrativeUnitIds.Remove(administrativeUnitId);
            await _ledger.SetAsync(deviceId, memberships, cancellationToken);
            await trace.WriteAsync(
                "Information",
                "Membership",
                "RemoveOwnedMembership",
                "Succeeded",
                administrativeUnitDetails,
                null,
                cancellationToken);
        }

        return new MembershipReconciliationResult(added, removed);
    }

    private static Dictionary<string, object?> MembershipDetails(
        Guid deviceId,
        string entraObjectId,
        string targetType,
        Guid? targetId,
        int ownedCount,
        string? machineName) =>
        new()
        {
            ["deviceId"] = deviceId,
            ["machineName"] = machineName,
            ["entraObjectId"] = entraObjectId,
            ["targetType"] = targetType,
            ["targetId"] = targetId,
            ["ownedByHybridDeviceTagger"] = ownedCount > 0
        };
}