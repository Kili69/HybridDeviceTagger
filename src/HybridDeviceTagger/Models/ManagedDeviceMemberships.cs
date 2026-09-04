namespace HybridDeviceTagger.Models;

/// <summary>Records Entra memberships created and therefore owned by HybridDeviceTagger.</summary>
public sealed record ManagedDeviceMemberships(
    string EntraObjectId,
    HashSet<Guid> EntraGroupIds,
    HashSet<Guid> AdministrativeUnitIds)
{
    /// <summary>Creates an empty ownership record for an Entra device object.</summary>
    public static ManagedDeviceMemberships Empty(string entraObjectId) =>
        new(entraObjectId, [], []);
}