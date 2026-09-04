/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
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
    public async Task<MembershipReconciliationResult> ReconcileAsync(
        Guid deviceId,
        string entraObjectId,
        IReadOnlySet<Guid> desiredGroupIds,
        IReadOnlySet<Guid> desiredAdministrativeUnitIds,
        CancellationToken cancellationToken,
        IRunTraceSession? trace = null)
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
                memberships.EntraGroupIds.Count + memberships.AdministrativeUnitIds.Count),
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
                memberships.EntraGroupIds.Contains(groupId) ? 1 : 0);
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
            var groupDetails = MembershipDetails(deviceId, entraObjectId, "Group", groupId, 1);
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
                memberships.AdministrativeUnitIds.Contains(administrativeUnitId) ? 1 : 0);
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
                1);
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
        int ownedCount) =>
        new()
        {
            ["deviceId"] = deviceId,
            ["entraObjectId"] = entraObjectId,
            ["targetType"] = targetType,
            ["targetId"] = targetId,
            ["ownedByHybridDeviceTagger"] = ownedCount > 0
        };
}