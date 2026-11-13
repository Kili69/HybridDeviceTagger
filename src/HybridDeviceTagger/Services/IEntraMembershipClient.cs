/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

namespace HybridDeviceTagger.Services;

/// <summary>Reads and changes direct Entra device memberships through Microsoft Graph.</summary>
public interface IEntraMembershipClient
{
    /// <summary>Determines whether a device is a direct member of an Entra group.</summary>
    /// <param name="groupId">The Entra security-group object identifier.</param>
    /// <param name="entraObjectId">The Entra device object identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> when the device is a direct member.</returns>
    Task<bool> IsGroupMemberAsync(
        Guid groupId,
        string entraObjectId,
        CancellationToken cancellationToken);

    /// <summary>Adds a device as a direct member of an Entra group.</summary>
    /// <param name="groupId">The Entra security-group object identifier.</param>
    /// <param name="entraObjectId">The Entra device object identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task representing the asynchronous change.</returns>
    Task AddGroupMemberAsync(
        Guid groupId,
        string entraObjectId,
        CancellationToken cancellationToken);

    /// <summary>Removes a device's direct membership from an Entra group.</summary>
    /// <param name="groupId">The Entra security-group object identifier.</param>
    /// <param name="entraObjectId">The Entra device object identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task representing the asynchronous change.</returns>
    Task RemoveGroupMemberAsync(
        Guid groupId,
        string entraObjectId,
        CancellationToken cancellationToken);

    /// <summary>Determines whether a device is a direct member of an administrative unit.</summary>
    /// <param name="administrativeUnitId">The administrative-unit object identifier.</param>
    /// <param name="entraObjectId">The Entra device object identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> when the device is a direct member.</returns>
    Task<bool> IsAdministrativeUnitMemberAsync(
        Guid administrativeUnitId,
        string entraObjectId,
        CancellationToken cancellationToken);

    /// <summary>Adds a device to a normal or restricted administrative unit.</summary>
    /// <param name="administrativeUnitId">The administrative-unit object identifier.</param>
    /// <param name="entraObjectId">The Entra device object identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task representing the asynchronous change.</returns>
    Task AddAdministrativeUnitMemberAsync(
        Guid administrativeUnitId,
        string entraObjectId,
        CancellationToken cancellationToken);

    /// <summary>Removes a device from a normal or restricted administrative unit.</summary>
    /// <param name="administrativeUnitId">The administrative-unit object identifier.</param>
    /// <param name="entraObjectId">The Entra device object identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task representing the asynchronous change.</returns>
    Task RemoveAdministrativeUnitMemberAsync(
        Guid administrativeUnitId,
        string entraObjectId,
        CancellationToken cancellationToken);
}