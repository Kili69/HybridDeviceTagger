/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

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
        IReadOnlyDictionary<string, string?> extensionAttributes,
        CancellationToken cancellationToken);
}