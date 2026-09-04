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
    private readonly DeviceMembershipReconciliationService _membershipService;
    private readonly TaggingOptions _options;
    private readonly ILogger<DeviceTaggingService> _logger;

    /// <summary>Initializes a new instance of the <see cref="DeviceTaggingService"/> class.</summary>
    /// <param name="directoryDeviceSource">The Active Directory device source.</param>
    /// <param name="entraDeviceClient">The Microsoft Entra device client.</param>
    /// <param name="membershipService">The Entra membership reconciler.</param>
    /// <param name="options">The configured OU tagging rules.</param>
    /// <param name="logger">The structured logger.</param>
    public DeviceTaggingService(
        IActiveDirectoryDeviceSource directoryDeviceSource,
        IEntraDeviceClient entraDeviceClient,
        DeviceMembershipReconciliationService membershipService,
        IOptions<TaggingOptions> options,
        ILogger<DeviceTaggingService> logger)
    {
        _directoryDeviceSource = directoryDeviceSource;
        _entraDeviceClient = entraDeviceClient;
        _membershipService = membershipService;
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
        var membershipsAdded = 0;
        var membershipsRemoved = 0;

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
                    "No Entra device found for AD device ID {DeviceId}",
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
            if (changedAttributes.Count > 0)
            {
                await _entraDeviceClient.SetExtensionAttributesAsync(
                    entraDevice.Id,
                    changedAttributes,
                    cancellationToken);
                _logger.LogInformation(
                    "Updated Entra device {DeviceId}. Changed attributes: {ChangedAttributes}",
                    directoryDevice.DeviceId,
                    string.Join(",", changedAttributes.Keys.Order(StringComparer.OrdinalIgnoreCase)));
            }

            var membershipResult = await _membershipService.ReconcileAsync(
                directoryDevice.DeviceId,
                entraDevice.Id,
                assignment.EntraGroupIds,
                assignment.AdministrativeUnitIds,
                cancellationToken);
            membershipsAdded += membershipResult.Added;
            membershipsRemoved += membershipResult.Removed;
            if (changedAttributes.Count > 0 || membershipResult.Changed)
            {
                updated++;
            }
            else
            {
                unchanged++;
            }
        }

        return new TaggingResult(
            assignments.Count,
            updated,
            unchanged,
            notFound,
            notHybridJoined,
            membershipsAdded,
            membershipsRemoved);
    }

    /// <summary>Builds one deduplicated, conflict-free attribute assignment per device.</summary>
    /// <remarks>
    /// Devices may match multiple rules. Different attributes are merged, while two different
    /// values for the same attribute fail the run to avoid order-dependent updates.
    /// </remarks>
    private async Task<Dictionary<Guid, DeviceTagAssignment>> GetAssignmentsAsync(
        CancellationToken cancellationToken)
    {
        var assignments = new Dictionary<Guid, DeviceTagAssignment>();
        var rules = _options.GetRules();
        for (var ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
        {
            var rule = rules[ruleIndex];
            _logger.LogInformation(
                "Searching Active Directory for tagging rule {RuleName} ({RuleNumber} of {RuleCount}): {RuleDescription}",
                rule.Name,
                ruleIndex + 1,
                rules.Count,
                rule.Description);
            var devices = await _directoryDeviceSource.GetHybridJoinedDevicesAsync(
                rule.BaseDn,
                cancellationToken);
            _logger.LogInformation(
                "Tagging rule {RuleName} discovered {DeviceCount} candidate devices",
                rule.Name,
                devices.Count);
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

                assignment.EntraGroupIds.UnionWith(rule.EntraGroupIds);
                assignment.AdministrativeUnitIds.UnionWith(rule.AdministrativeUnitIds);
            }
        }

        return assignments;
    }

    /// <summary>Accumulates all desired extension attributes for one discovered device.</summary>
    private sealed record DeviceTagAssignment(DirectoryDevice Device)
    {
        /// <summary>Gets the case-insensitive assignment map merged from matching rules.</summary>
        public Dictionary<string, string> ExtensionAttributes { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Gets desired Entra security-group memberships.</summary>
        public HashSet<Guid> EntraGroupIds { get; } = [];

        /// <summary>Gets desired normal or restricted administrative-unit memberships.</summary>
        public HashSet<Guid> AdministrativeUnitIds { get; } = [];
    }
}