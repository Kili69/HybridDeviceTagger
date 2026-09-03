using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Services;

public sealed class DeviceTaggingService
{
    private readonly IActiveDirectoryDeviceSource _directoryDeviceSource;
    private readonly IEntraDeviceClient _entraDeviceClient;
    private readonly TaggingOptions _options;
    private readonly ILogger<DeviceTaggingService> _logger;

    public DeviceTaggingService(
        IActiveDirectoryDeviceSource directoryDeviceSource,
        IEntraDeviceClient entraDeviceClient,
        IOptions<TaggingOptions> options,
        ILogger<DeviceTaggingService> logger)
    {
        _directoryDeviceSource = directoryDeviceSource;
        _entraDeviceClient = entraDeviceClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TaggingResult> TagDevicesAsync(CancellationToken cancellationToken)
    {
        var directoryDevices = await _directoryDeviceSource
            .GetHybridJoinedDevicesAsync(cancellationToken);
        var updated = 0;
        var unchanged = 0;
        var notFound = 0;
        var notHybridJoined = 0;

        foreach (var directoryDevice in directoryDevices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entraDevice = await _entraDeviceClient.FindByDeviceIdAsync(
                directoryDevice.DeviceId,
                cancellationToken);

            if (entraDevice is null)
            {
                notFound++;
                _logger.LogWarning(
                    "No Entra device found for AD computer {ComputerName} with device ID {DeviceId}",
                    directoryDevice.Name,
                    directoryDevice.DeviceId);
                continue;
            }

            if (!string.Equals(entraDevice.TrustType, "ServerAd", StringComparison.OrdinalIgnoreCase))
            {
                notHybridJoined++;
                _logger.LogWarning(
                    "Entra device {DeviceId} has trust type {TrustType}; expected ServerAd",
                    directoryDevice.DeviceId,
                    entraDevice.TrustType);
                continue;
            }

            if (string.Equals(
                entraDevice.ExtensionAttribute1,
                _options.ExtensionAttribute1Value,
                StringComparison.Ordinal))
            {
                unchanged++;
                continue;
            }

            await _entraDeviceClient.SetExtensionAttribute1Async(
                entraDevice.Id,
                _options.ExtensionAttribute1Value,
                cancellationToken);
            updated++;
        }

        return new TaggingResult(
            directoryDevices.Count,
            updated,
            unchanged,
            notFound,
            notHybridJoined);
    }
}