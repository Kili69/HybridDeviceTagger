using HybridDeviceTagger.Models;

namespace HybridDeviceTagger.Services;

/// <summary>Persists memberships that HybridDeviceTagger is allowed to remove later.</summary>
public interface IDeviceMembershipLedger
{
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