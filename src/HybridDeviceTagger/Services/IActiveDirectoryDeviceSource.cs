/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using HybridDeviceTagger.Models;

namespace HybridDeviceTagger.Services;

/// <summary>Discovers hybrid-joined computer objects in Active Directory.</summary>
public interface IActiveDirectoryDeviceSource
{
    /// <summary>Gets hybrid-joined computers in and below matching Active Directory OUs.</summary>
    /// <param name="baseDn">
    /// A domain-specific distinguished name or an OU-only path resolved in every forest domain.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The discovered directory devices.</returns>
    Task<IReadOnlyList<DirectoryDevice>> GetHybridJoinedDevicesAsync(
        string baseDn,
        CancellationToken cancellationToken);
}