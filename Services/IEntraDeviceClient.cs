using HybridDeviceTagger.Models;

namespace HybridDeviceTagger.Services;

public interface IEntraDeviceClient
{
    Task<EntraDevice?> FindByDeviceIdAsync(Guid deviceId, CancellationToken cancellationToken);

    Task SetExtensionAttribute1Async(
        string entraObjectId,
        string value,
        CancellationToken cancellationToken);
}