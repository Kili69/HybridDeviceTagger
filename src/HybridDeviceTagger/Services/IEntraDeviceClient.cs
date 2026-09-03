using HybridDeviceTagger.Models;

namespace HybridDeviceTagger.Services;

/// <summary>Reads and updates device objects through Microsoft Graph.</summary>
public interface IEntraDeviceClient
{
    /// <summary>Finds an Entra device by its directory device identifier.</summary>
    /// <param name="deviceId">The shared device identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The matching device, or <see langword="null"/> when none exists.</returns>
    Task<EntraDevice?> FindByDeviceIdAsync(Guid deviceId, CancellationToken cancellationToken);

    /// <summary>Sets one or more extension attributes on an Entra device.</summary>
    /// <param name="entraObjectId">The Entra object identifier.</param>
    /// <param name="extensionAttributes">The Graph attribute names and values to write.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task SetExtensionAttributesAsync(
        string entraObjectId,
        IReadOnlyDictionary<string, string> extensionAttributes,
        CancellationToken cancellationToken);
}