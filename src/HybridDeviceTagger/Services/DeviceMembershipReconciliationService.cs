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
        CancellationToken cancellationToken)
    {
        var memberships = await _ledger.GetAsync(deviceId, cancellationToken);
        if (memberships is null ||
            !string.Equals(memberships.EntraObjectId, entraObjectId, StringComparison.OrdinalIgnoreCase))
        {
            memberships = ManagedDeviceMemberships.Empty(entraObjectId);
        }

        var added = 0;
        var removed = 0;
        foreach (var groupId in desiredGroupIds)
        {
            if (!await _membershipClient.IsGroupMemberAsync(
                    groupId,
                    entraObjectId,
                    cancellationToken))
            {
                await _membershipClient.AddGroupMemberAsync(
                    groupId,
                    entraObjectId,
                    cancellationToken);
                memberships.EntraGroupIds.Add(groupId);
                await _ledger.SetAsync(deviceId, memberships, cancellationToken);
                added++;
            }
        }

        foreach (var groupId in memberships.EntraGroupIds.Except(desiredGroupIds).ToArray())
        {
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
        }

        foreach (var administrativeUnitId in desiredAdministrativeUnitIds)
        {
            if (!await _membershipClient.IsAdministrativeUnitMemberAsync(
                    administrativeUnitId,
                    entraObjectId,
                    cancellationToken))
            {
                await _membershipClient.AddAdministrativeUnitMemberAsync(
                    administrativeUnitId,
                    entraObjectId,
                    cancellationToken);
                memberships.AdministrativeUnitIds.Add(administrativeUnitId);
                await _ledger.SetAsync(deviceId, memberships, cancellationToken);
                added++;
            }
        }

        foreach (var administrativeUnitId in memberships.AdministrativeUnitIds
                     .Except(desiredAdministrativeUnitIds)
                     .ToArray())
        {
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
        }

        return new MembershipReconciliationResult(added, removed);
    }
}