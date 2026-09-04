/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace HybridDeviceTagger.Services;

/// <summary>Coordinates idempotent tagging of hybrid-joined Microsoft Entra devices.</summary>
public sealed class DeviceTaggingService
{
    private readonly IActiveDirectoryDeviceSource _directoryDeviceSource;
    private readonly IEntraDeviceClient _entraDeviceClient;
    private readonly DeviceMembershipReconciliationService _membershipService;
    private readonly IDeviceMembershipLedger _ledger;
    private readonly TaggingOptions _options;
    private readonly ILogger<DeviceTaggingService> _logger;

    /// <summary>Initializes a new instance of the <see cref="DeviceTaggingService"/> class.</summary>
    /// <param name="directoryDeviceSource">The Active Directory device source.</param>
    /// <param name="entraDeviceClient">The Microsoft Entra device client.</param>
    /// <param name="membershipService">The Entra membership reconciler.</param>
    /// <param name="ledger">The managed Entra state ledger.</param>
    /// <param name="options">The configured OU tagging rules.</param>
    /// <param name="logger">The structured logger.</param>
    public DeviceTaggingService(
        IActiveDirectoryDeviceSource directoryDeviceSource,
        IEntraDeviceClient entraDeviceClient,
        DeviceMembershipReconciliationService membershipService,
        IDeviceMembershipLedger ledger,
        IOptions<TaggingOptions> options,
        ILogger<DeviceTaggingService> logger)
    {
        _directoryDeviceSource = directoryDeviceSource;
        _entraDeviceClient = entraDeviceClient;
        _membershipService = membershipService;
        _ledger = ledger;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Tags matching <c>ServerAd</c> devices whose configured values differ.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <param name="trace">The optional structured diagnostic trace for this run.</param>
    /// <returns>Aggregate discovery, update, and skip counts.</returns>
    /// <remarks>Devices already carrying the exact configured value are not patched.</remarks>
    public async Task<TaggingResult> TagDevicesAsync(
        CancellationToken cancellationToken,
        IRunTraceSession? trace = null)
    {
        trace ??= RunTrace.None;
        await trace.WriteAsync(
            "Information",
            "Discovery",
            "LoadAssignments",
            "Started",
            new Dictionary<string, object?> { ["configuredRuleCount"] = _options.GetRules().Count },
            null,
            cancellationToken);
        var assignments = await GetAssignmentsAsync(cancellationToken, trace);
        var updated = 0;
        var unchanged = 0;
        var notFound = 0;
        var notHybridJoined = 0;
        var membershipsAdded = 0;
        var membershipsRemoved = 0;
        var removedFromScope = 0;
        var managedDevices = new Dictionary<Guid, ManagedDeviceMemberships>(
            await _ledger.GetAllAsync(cancellationToken));
        await trace.WriteAsync(
            "Information",
            "Discovery",
            "LoadAssignments",
            "Succeeded",
            new Dictionary<string, object?>
            {
                ["assignedDeviceCount"] = assignments.Count,
                ["ledgerDeviceCount"] = managedDevices.Count
            },
            null,
            cancellationToken);

        foreach (var assignment in assignments.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directoryDevice = assignment.Device;
            managedDevices.Remove(directoryDevice.DeviceId);
            await trace.WriteAsync(
                "Information",
                "Device",
                "FindEntraDevice",
                "Started",
                DeviceDetails(directoryDevice.DeviceId, null),
                null,
                cancellationToken);
            var entraDevice = await _entraDeviceClient.FindByDeviceIdAsync(
                directoryDevice.DeviceId,
                cancellationToken);

            if (entraDevice is null)
            {
                notFound++;
                _logger.LogWarning(
                    "No Entra device found for AD device ID {DeviceId}",
                    directoryDevice.DeviceId);
                await trace.WriteAsync(
                    "Warning",
                    "Device",
                    "FindEntraDevice",
                    "NotFound",
                    DeviceDetails(directoryDevice.DeviceId, null),
                    null,
                    cancellationToken);
                continue;
            }

            await trace.WriteAsync(
                "Information",
                "Device",
                "FindEntraDevice",
                "Succeeded",
                DeviceDetails(directoryDevice.DeviceId, entraDevice.Id),
                null,
                cancellationToken);

            if (!string.Equals(entraDevice.TrustType, "ServerAd", StringComparison.OrdinalIgnoreCase))
            {
                notHybridJoined++;
                _logger.LogWarning(
                    "Entra device {DeviceId} has trust type {TrustType}; expected ServerAd",
                    directoryDevice.DeviceId,
                    entraDevice.TrustType);
                var details = DeviceDetails(directoryDevice.DeviceId, entraDevice.Id);
                details["trustType"] = entraDevice.TrustType;
                details["expectedTrustType"] = "ServerAd";
                await trace.WriteAsync(
                    "Warning",
                    "Device",
                    "ValidateTrustType",
                    "Skipped",
                    details,
                    null,
                    cancellationToken);
                continue;
            }

            var managedState = await _ledger.GetAsync(directoryDevice.DeviceId, cancellationToken);
            if (managedState is null ||
                !string.Equals(managedState.EntraObjectId, entraDevice.Id, StringComparison.OrdinalIgnoreCase))
            {
                managedState = ManagedDeviceMemberships.Empty(entraDevice.Id);
            }

            var changedAttributes = assignment.ExtensionAttributes
                .Where(attribute =>
                    !entraDevice.ExtensionAttributes.TryGetValue(attribute.Key, out var currentValue) ||
                    !string.Equals(currentValue, attribute.Value, StringComparison.Ordinal))
                .ToDictionary(
                    attribute => attribute.Key,
                    attribute => (string?)attribute.Value,
                    StringComparer.OrdinalIgnoreCase);
            foreach (var (attributeName, managedValue) in managedState.ExtensionAttributes
                         .Where(attribute => !assignment.ExtensionAttributes.ContainsKey(attribute.Key))
                         .ToArray())
            {
                if (entraDevice.ExtensionAttributes.TryGetValue(attributeName, out var currentValue) &&
                    string.Equals(currentValue, managedValue, StringComparison.Ordinal))
                {
                    changedAttributes[attributeName] = null;
                }

                managedState.ExtensionAttributes.Remove(attributeName);
            }

            var attributeDetails = DeviceDetails(directoryDevice.DeviceId, entraDevice.Id);
            attributeDetails["desiredAttributes"] = assignment.ExtensionAttributes
                .OrderBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase)
                .Select(attribute => new
                {
                    name = attribute.Key,
                    desiredValueSha256 = Fingerprint(attribute.Value),
                    currentValueSha256 = entraDevice.ExtensionAttributes.TryGetValue(
                        attribute.Key,
                        out var currentValue)
                            ? Fingerprint(currentValue)
                            : null,
                    action = changedAttributes.ContainsKey(attribute.Key) ? "Set" : "None"
                })
                .ToArray();
            attributeDetails["attributesToClear"] = changedAttributes
                .Where(attribute => attribute.Value is null)
                .Select(attribute => attribute.Key)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            await trace.WriteAsync(
                "Information",
                "Device",
                "ReconcileExtensionAttributes",
                changedAttributes.Count > 0 ? "ChangesRequired" : "Unchanged",
                attributeDetails,
                null,
                cancellationToken);

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
                await trace.WriteAsync(
                    "Information",
                    "Device",
                    "PatchExtensionAttributes",
                    "Succeeded",
                    new Dictionary<string, object?>
                    {
                        ["deviceId"] = directoryDevice.DeviceId,
                        ["entraObjectId"] = entraDevice.Id,
                        ["attributeNames"] = changedAttributes.Keys
                            .Order(StringComparer.OrdinalIgnoreCase)
                            .ToArray()
                    },
                    null,
                    cancellationToken);
            }

            foreach (var (attributeName, value) in assignment.ExtensionAttributes)
            {
                managedState.ExtensionAttributes[attributeName] = value;
            }

            await _ledger.SetAsync(directoryDevice.DeviceId, managedState, cancellationToken);
            await trace.WriteAsync(
                "Information",
                "Ledger",
                "PersistManagedExtensionAttributes",
                "Succeeded",
                new Dictionary<string, object?>
                {
                    ["deviceId"] = directoryDevice.DeviceId,
                    ["entraObjectId"] = entraDevice.Id,
                    ["managedAttributeNames"] = managedState.ExtensionAttributes.Keys
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                },
                null,
                cancellationToken);

            var membershipResult = await _membershipService.ReconcileAsync(
                directoryDevice.DeviceId,
                entraDevice.Id,
                assignment.EntraGroupIds,
                assignment.AdministrativeUnitIds,
                cancellationToken,
                trace);
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

        foreach (var (deviceId, managedState) in managedDevices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            removedFromScope++;
            await trace.WriteAsync(
                "Information",
                "Cleanup",
                "ProcessDeviceOutsideConfiguredOus",
                "Started",
                DeviceDetails(deviceId, managedState.EntraObjectId),
                null,
                cancellationToken);
            var entraDevice = await _entraDeviceClient.FindByDeviceIdAsync(deviceId, cancellationToken);
            if (entraDevice is null ||
                !string.Equals(managedState.EntraObjectId, entraDevice.Id, StringComparison.OrdinalIgnoreCase))
            {
                managedState.ExtensionAttributes.Clear();
                managedState.EntraGroupIds.Clear();
                managedState.AdministrativeUnitIds.Clear();
                await _ledger.SetAsync(deviceId, managedState, cancellationToken);
                _logger.LogInformation(
                    "Removed obsolete ownership state for device {DeviceId} because its Entra object no longer exists",
                    deviceId);
                await trace.WriteAsync(
                    "Information",
                    "Cleanup",
                    "RemoveObsoleteLedgerState",
                    "Succeeded",
                    DeviceDetails(deviceId, managedState.EntraObjectId),
                    null,
                    cancellationToken);
                continue;
            }

            var attributesToClear = managedState.ExtensionAttributes
                .Where(attribute =>
                    entraDevice.ExtensionAttributes.TryGetValue(attribute.Key, out var currentValue) &&
                    string.Equals(currentValue, attribute.Value, StringComparison.Ordinal))
                .ToDictionary(
                    attribute => attribute.Key,
                    _ => (string?)null,
                    StringComparer.OrdinalIgnoreCase);
            if (attributesToClear.Count > 0)
            {
                await _entraDeviceClient.SetExtensionAttributesAsync(
                    entraDevice.Id,
                    attributesToClear,
                    cancellationToken);
            }

            var cleanupDetails = DeviceDetails(deviceId, entraDevice.Id);
            cleanupDetails["clearedAttributeNames"] = attributesToClear.Keys
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            cleanupDetails["preservedManuallyChangedAttributeNames"] = managedState.ExtensionAttributes
                .Keys.Except(attributesToClear.Keys, StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            await trace.WriteAsync(
                "Information",
                "Cleanup",
                "ClearOwnedExtensionAttributes",
                attributesToClear.Count > 0 ? "Succeeded" : "NoOwnedValuesMatched",
                cleanupDetails,
                null,
                cancellationToken);

            managedState.ExtensionAttributes.Clear();
            await _ledger.SetAsync(deviceId, managedState, cancellationToken);
            var membershipResult = await _membershipService.ReconcileAsync(
                deviceId,
                entraDevice.Id,
                new HashSet<Guid>(),
                new HashSet<Guid>(),
                cancellationToken,
                trace);
            membershipsRemoved += membershipResult.Removed;
            if (attributesToClear.Count > 0 || membershipResult.Changed)
            {
                updated++;
            }

            _logger.LogInformation(
                "Cleaned managed Entra state for device {DeviceId} after it left all configured OUs",
                deviceId);
        }

        return new TaggingResult(
            assignments.Count,
            updated,
            unchanged,
            notFound,
            notHybridJoined,
            membershipsAdded,
            membershipsRemoved,
            removedFromScope);
    }

    /// <summary>Builds one deduplicated, conflict-free attribute assignment per device.</summary>
    /// <remarks>
    /// Devices may match multiple rules. Different attributes are merged, while two different
    /// values for the same attribute fail the run to avoid order-dependent updates.
    /// </remarks>
    private async Task<Dictionary<Guid, DeviceTagAssignment>> GetAssignmentsAsync(
        CancellationToken cancellationToken,
        IRunTraceSession trace)
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
            await trace.WriteAsync(
                "Information",
                "Discovery",
                "SearchActiveDirectoryRule",
                "Started",
                new Dictionary<string, object?>
                {
                    ["ruleName"] = rule.Name,
                    ["ruleNumber"] = ruleIndex + 1,
                    ["ruleCount"] = rules.Count,
                    ["baseDn"] = rule.BaseDn
                },
                null,
                cancellationToken);
            var devices = await _directoryDeviceSource.GetHybridJoinedDevicesAsync(
                rule.BaseDn,
                cancellationToken);
            _logger.LogInformation(
                "Tagging rule {RuleName} discovered {DeviceCount} candidate devices",
                rule.Name,
                devices.Count);
            await trace.WriteAsync(
                "Information",
                "Discovery",
                "SearchActiveDirectoryRule",
                "Succeeded",
                new Dictionary<string, object?>
                {
                    ["ruleName"] = rule.Name,
                    ["baseDn"] = rule.BaseDn,
                    ["deviceCount"] = devices.Count,
                    ["deviceIds"] = devices.Select(device => device.DeviceId).ToArray()
                },
                null,
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

                assignment.EntraGroupIds.UnionWith(rule.EntraGroupIds);
                assignment.AdministrativeUnitIds.UnionWith(rule.AdministrativeUnitIds);
            }
        }

        return assignments;
    }

    private static Dictionary<string, object?> DeviceDetails(Guid deviceId, string? entraObjectId) =>
        new()
        {
            ["deviceId"] = deviceId,
            ["entraObjectId"] = entraObjectId
        };

    private static string? Fingerprint(string? value) =>
        value is null
            ? null
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

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