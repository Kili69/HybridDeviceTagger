/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
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