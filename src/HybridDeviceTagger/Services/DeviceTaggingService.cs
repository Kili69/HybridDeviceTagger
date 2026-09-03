using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Services;

/// <summary>Coordinates idempotent tagging of hybrid-joined Microsoft Entra devices.</summary>
public sealed class DeviceTaggingService
{
    private readonly IActiveDirectoryDeviceSource _directoryDeviceSource;
    private readonly IEntraDeviceClient _entraDeviceClient;
    private readonly TaggingOptions _options;
    private readonly ILogger<DeviceTaggingService> _logger;

    /// <summary>Initializes a new instance of the <see cref="DeviceTaggingService"/> class.</summary>
    /// <param name="directoryDeviceSource">The Active Directory device source.</param>
    /// <param name="entraDeviceClient">The Microsoft Entra device client.</param>
    /// <param name="options">The configured OU tagging rules.</param>
    /// <param name="logger">The structured logger.</param>
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

    /// <summary>Tags matching <c>ServerAd</c> devices whose configured values differ.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Aggregate discovery, update, and skip counts.</returns>
    /// <remarks>Devices already carrying the exact configured value are not patched.</remarks>
    public async Task<TaggingResult> TagDevicesAsync(CancellationToken cancellationToken)
    {
        var assignments = await GetAssignmentsAsync(cancellationToken);
        var updated = 0;
        var unchanged = 0;
        var notFound = 0;
        var notHybridJoined = 0;

        foreach (var assignment in assignments.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directoryDevice = assignment.Device;
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

            var changedAttributes = assignment.ExtensionAttributes
                .Where(attribute =>
                    !entraDevice.ExtensionAttributes.TryGetValue(attribute.Key, out var currentValue) ||
                    !string.Equals(currentValue, attribute.Value, StringComparison.Ordinal))
                .ToDictionary(
                    attribute => attribute.Key,
                    attribute => attribute.Value,
                    StringComparer.OrdinalIgnoreCase);
            if (changedAttributes.Count == 0)
            {
                unchanged++;
                continue;
            }

            await _entraDeviceClient.SetExtensionAttributesAsync(
                entraDevice.Id,
                changedAttributes,
                cancellationToken);
            updated++;
        }

        return new TaggingResult(
            assignments.Count,
            updated,
            unchanged,
            notFound,
            notHybridJoined);
    }

    private async Task<Dictionary<Guid, DeviceTagAssignment>> GetAssignmentsAsync(
        CancellationToken cancellationToken)
    {
        var assignments = new Dictionary<Guid, DeviceTagAssignment>();
        foreach (var rule in _options.GetRules())
        {
            var devices = await _directoryDeviceSource.GetHybridJoinedDevicesAsync(
                rule.BaseDn,
                cancellationToken);
            foreach (var device in devices)
            {
                if (!assignments.TryGetValue(device.DeviceId, out var assignment))
                {
                    assignment = new DeviceTagAssignment(device);
                    assignments.Add(device.DeviceId, assignment);
                }

                foreach (var (attributeName, value) in rule.ExtensionAttributes)
                {
                    if (assignment.ExtensionAttributes.TryGetValue(attributeName, out var existing) &&
                        !string.Equals(existing, value, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Conflicting values are configured for {attributeName} on device " +
                            $"{device.DeviceId:D}.");
                    }

                    assignment.ExtensionAttributes[attributeName] = value;
                }
            }
        }

        return assignments;
    }

    private sealed record DeviceTagAssignment(DirectoryDevice Device)
    {
        public Dictionary<string, string> ExtensionAttributes { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}