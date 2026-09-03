using HybridDeviceTagger.Models;

namespace HybridDeviceTagger.Services;

public interface IActiveDirectoryDeviceSource
{
    Task<IReadOnlyList<DirectoryDevice>> GetHybridJoinedDevicesAsync(
        CancellationToken cancellationToken);
}