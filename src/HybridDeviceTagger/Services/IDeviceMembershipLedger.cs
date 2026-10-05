/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
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