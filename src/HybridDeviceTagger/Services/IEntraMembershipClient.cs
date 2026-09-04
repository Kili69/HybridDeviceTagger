namespace HybridDeviceTagger.Services;

/// <summary>Reads and changes direct Entra device memberships through Microsoft Graph.</summary>
public interface IEntraMembershipClient
{
    /// <summary>Determines whether a device is a direct member of an Entra group.</summary>
    Task<bool> IsGroupMemberAsync(
        Guid groupId,
        string entraObjectId,
        CancellationToken cancellationToken);

    /// <summary>Adds a device as a direct member of an Entra group.</summary>
    Task AddGroupMemberAsync(
        Guid groupId,
        string entraObjectId,
        CancellationToken cancellationToken);

    /// <summary>Removes a device's direct membership from an Entra group.</summary>
    Task RemoveGroupMemberAsync(
        Guid groupId,
        string entraObjectId,
        CancellationToken cancellationToken);

    /// <summary>Determines whether a device is a direct member of an administrative unit.</summary>
    Task<bool> IsAdministrativeUnitMemberAsync(
        Guid administrativeUnitId,
        string entraObjectId,
        CancellationToken cancellationToken);

    /// <summary>Adds a device to a normal or restricted administrative unit.</summary>
    Task AddAdministrativeUnitMemberAsync(
        Guid administrativeUnitId,
        string entraObjectId,
        CancellationToken cancellationToken);

    /// <summary>Removes a device from a normal or restricted administrative unit.</summary>
    Task RemoveAdministrativeUnitMemberAsync(
        Guid administrativeUnitId,
        string entraObjectId,
        CancellationToken cancellationToken);
}