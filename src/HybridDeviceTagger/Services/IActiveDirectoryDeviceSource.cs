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