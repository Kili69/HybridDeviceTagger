using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using HybridDeviceTagger.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Tests;

public sealed class DeviceTaggingServiceTests
{
    [Fact]
    public async Task TagDevicesAsync_WithNoRules_DoesNotReadDirectoriesOrCleanLedger()
    {
        var deviceId = Guid.NewGuid();
        var directorySource = new StubDirectoryDeviceSource([]);
        var ledger = new StubMembershipLedger();
        ledger.Values[deviceId] = new ManagedDeviceMemberships(
            deviceId.ToString("D"),
            [],
            [])
        {
            ExtensionAttributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["extensionAttribute1"] = "Managed"
            }
        };
        var service = new DeviceTaggingService(
            directorySource,
            new StubEntraDeviceClient(new Dictionary<Guid, EntraDevice>()),
            CreateMembershipService(ledger),
            ledger,
            new OptionsTaggingRuleProvider(
                Microsoft.Extensions.Options.Options.Create(new TaggingOptions())),
            NullLogger<DeviceTaggingService>.Instance);

        var result = await service.TagDevicesAsync(CancellationToken.None);

        Assert.Equal(new TaggingResult(0, 0, 0, 0, 0), result);
        Assert.Empty(directorySource.RequestedBaseDns);
        Assert.True(ledger.Values.ContainsKey(deviceId));
    }

    [Fact]
    public async Task TagDevicesAsync_UpdatesOnlyUntaggedHybridDevice()
    {
        var updateDeviceId = Guid.NewGuid();
        var unchangedDeviceId = Guid.NewGuid();
        var missingDeviceId = Guid.NewGuid();
        var nonHybridDeviceId = Guid.NewGuid();
        var directorySource = new StubDirectoryDeviceSource(
        [
            CreateDirectoryDevice(updateDeviceId),
            CreateDirectoryDevice(unchangedDeviceId),
            CreateDirectoryDevice(missingDeviceId),
            CreateDirectoryDevice(nonHybridDeviceId)
        ]);
        var entraClient = new StubEntraDeviceClient(new Dictionary<Guid, EntraDevice>
        {
            [updateDeviceId] = CreateEntraDevice(updateDeviceId, "old value", "ServerAd"),
            [unchangedDeviceId] = CreateEntraDevice(unchangedDeviceId, "Tier 0", "ServerAd"),
            [nonHybridDeviceId] = CreateEntraDevice(nonHybridDeviceId, null, "AzureAd")
        });
        var ledger = new StubMembershipLedger();
        var service = new DeviceTaggingService(
            directorySource,
            entraClient,
            CreateMembershipService(ledger),
            ledger,
            new OptionsTaggingRuleProvider(Microsoft.Extensions.Options.Options.Create(CreateOptions(
                ("OU=Computers,DC=example,DC=com", new Dictionary<string, string>
                {
                    ["extensionAttribute1"] = "Tier 0"
                })))),
            NullLogger<DeviceTaggingService>.Instance);
        var trace = new RecordingRunTraceSession();

        var result = await service.TagDevicesAsync(CancellationToken.None, trace);

        Assert.Equal(new TaggingResult(4, 1, 1, 1, 1), result);
        var update = Assert.Single(entraClient.Updates);
        Assert.Equal(updateDeviceId.ToString("D"), update.ObjectId);
        Assert.Equal("Tier 0", update.Attributes["extensionAttribute1"]);
        Assert.Equal(
            "Tier 0",
            ledger.Values[updateDeviceId].ExtensionAttributes["extensionAttribute1"]);
        Assert.Equal(
            "Tier 0",
            ledger.Values[unchangedDeviceId].ExtensionAttributes["extensionAttribute1"]);
        Assert.Contains(trace.Events, traceEvent =>
            traceEvent.Phase == "Discovery" &&
            traceEvent.Action == "SearchActiveDirectoryRule" &&
            traceEvent.Outcome == "Succeeded");
        Assert.Contains(trace.Events, traceEvent =>
            traceEvent.Phase == "Device" &&
            traceEvent.Action == "PatchExtensionAttributes" &&
            traceEvent.Outcome == "Succeeded" &&
            Equals(traceEvent.Details?["machineName"], $"Computer-{updateDeviceId:N}"));
        Assert.Contains(trace.Events, traceEvent =>
            traceEvent.Phase == "Membership" &&
            Equals(traceEvent.Details?["machineName"], $"Computer-{updateDeviceId:N}"));
        Assert.DoesNotContain("Tier 0", trace.SerializedDetails, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TagDevicesAsync_AppliesMultipleOuRulesAndMergesOverlappingAssignments()
    {
        const string tier0Ou = "OU=Tier0,OU=Admin,DC=example,DC=com";
        const string serverOu = "OU=Server,DC=example,DC=com";
        var tier0DeviceId = Guid.NewGuid();
        var nestedTier0DeviceId = Guid.NewGuid();
        var serverDeviceId = Guid.NewGuid();
        var directorySource = new StubDirectoryDeviceSource(new Dictionary<string, IReadOnlyList<DirectoryDevice>>(
            StringComparer.OrdinalIgnoreCase)
        {
            [tier0Ou] =
            [
                CreateDirectoryDevice(tier0DeviceId, tier0Ou),
                CreateDirectoryDevice(nestedTier0DeviceId, $"OU=Workstations,{tier0Ou}")
            ],
            [serverOu] = [CreateDirectoryDevice(serverDeviceId, serverOu)]
        });
        var entraClient = new StubEntraDeviceClient(new Dictionary<Guid, EntraDevice>
        {
            [tier0DeviceId] = CreateEntraDevice(tier0DeviceId, "ServerAd",
                ("extensionAttribute1", "old"), ("extensionAttribute2", "Tier0")),
            [nestedTier0DeviceId] = CreateEntraDevice(nestedTier0DeviceId, "ServerAd",
                ("extensionAttribute1", "PAW"), ("extensionAttribute2", "old")),
            [serverDeviceId] = CreateEntraDevice(serverDeviceId, "ServerAd",
                ("extensionAttribute2", "old"))
        });
        var ledger = new StubMembershipLedger();
        var service = new DeviceTaggingService(
            directorySource,
            entraClient,
            CreateMembershipService(ledger),
            ledger,
            new OptionsTaggingRuleProvider(Microsoft.Extensions.Options.Options.Create(CreateOptions(
                (tier0Ou, new Dictionary<string, string>
                {
                    ["extensionAttribute1"] = "PAW",
                    ["extensionAttribute2"] = "Tier0"
                }),
                (serverOu, new Dictionary<string, string>
                {
                    ["extensionAttribute2"] = "Tier1"
                })))),
            NullLogger<DeviceTaggingService>.Instance);

        var result = await service.TagDevicesAsync(CancellationToken.None);

        Assert.Equal(new TaggingResult(3, 3, 0, 0, 0), result);
        Assert.Equal([tier0Ou, serverOu], directorySource.RequestedBaseDns);
        Assert.Equal(3, entraClient.Updates.Count);
        Assert.Equal("PAW", entraClient.Updates.Single(update =>
            update.ObjectId == tier0DeviceId.ToString("D")).Attributes["extensionAttribute1"]);
        Assert.DoesNotContain("extensionAttribute2", entraClient.Updates.Single(update =>
            update.ObjectId == tier0DeviceId.ToString("D")).Attributes.Keys);
        Assert.Equal("Tier0", entraClient.Updates.Single(update =>
            update.ObjectId == nestedTier0DeviceId.ToString("D")).Attributes["extensionAttribute2"]);
        Assert.Equal("Tier1", entraClient.Updates.Single(update =>
            update.ObjectId == serverDeviceId.ToString("D")).Attributes["extensionAttribute2"]);
    }

    [Fact]
    public async Task TagDevicesAsync_ThrowsForConflictingOverlappingRules()
    {
        var deviceId = Guid.NewGuid();
        var device = CreateDirectoryDevice(deviceId, "OU=Child,OU=Parent,DC=example,DC=com");
        var directorySource = new StubDirectoryDeviceSource(new Dictionary<string, IReadOnlyList<DirectoryDevice>>
        {
            ["OU=Parent,DC=example,DC=com"] = [device],
            ["OU=Child,OU=Parent,DC=example,DC=com"] = [device]
        });
        var ledger = new StubMembershipLedger();
        var service = new DeviceTaggingService(
            directorySource,
            new StubEntraDeviceClient(new Dictionary<Guid, EntraDevice>()),
            CreateMembershipService(ledger),
            ledger,
            new OptionsTaggingRuleProvider(Microsoft.Extensions.Options.Options.Create(CreateOptions(
                ("OU=Parent,DC=example,DC=com", new Dictionary<string, string>
                {
                    ["extensionAttribute2"] = "Tier1"
                }),
                ("OU=Child,OU=Parent,DC=example,DC=com", new Dictionary<string, string>
                {
                    ["extensionAttribute2"] = "Tier0"
                })))),
            NullLogger<DeviceTaggingService>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.TagDevicesAsync(CancellationToken.None));

        Assert.Contains("extensionAttribute2", exception.Message);
    }

    [Fact]
    public async Task TagDevicesAsync_LoadsRulesFromAzureJsonSetting()
    {
        const string baseDn = "OU=Server,DC=example,DC=com";
        var deviceId = Guid.NewGuid();
        var directorySource = new StubDirectoryDeviceSource(
            new Dictionary<string, IReadOnlyList<DirectoryDevice>>
            {
                [baseDn] = [CreateDirectoryDevice(deviceId, baseDn)]
            });
        var entraClient = new StubEntraDeviceClient(new Dictionary<Guid, EntraDevice>
        {
            [deviceId] = CreateEntraDevice(deviceId, "ServerAd", ("extensionAttribute2", null))
        });
        var options = new TaggingOptions
        {
            RulesJson = """
                [{"name":"Tier1 servers","description":"Tags Tier 1 servers.","baseDn":"OU=Server,DC=example,DC=com","extensionAttributes":{"extensionAttribute2":"Tier1"}}]
                """
        };
        var ledger = new StubMembershipLedger();
        var service = new DeviceTaggingService(
            directorySource,
            entraClient,
            CreateMembershipService(ledger),
            ledger,
            new OptionsTaggingRuleProvider(Microsoft.Extensions.Options.Options.Create(options)),
            NullLogger<DeviceTaggingService>.Instance);

        await service.TagDevicesAsync(CancellationToken.None);

        var update = Assert.Single(entraClient.Updates);
        Assert.Equal("Tier1", update.Attributes["extensionAttribute2"]);
    }

    [Fact]
    public async Task TagDevicesAsync_RemovesOwnedStateWhenDeviceLeavesAllTargetOus()
    {
        var deviceId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var administrativeUnitId = Guid.NewGuid();
        var entraDevice = CreateEntraDevice(
            deviceId,
            "ServerAd",
            ("extensionAttribute1", "Tier 0"));
        var entraClient = new StubEntraDeviceClient(new Dictionary<Guid, EntraDevice>
        {
            [deviceId] = entraDevice
        });
        var membershipClient = new StubEntraMembershipClient();
        membershipClient.GroupMemberships.Add(groupId);
        membershipClient.AdministrativeUnitMemberships.Add(administrativeUnitId);
        var ledger = new StubMembershipLedger();
        ledger.Values[deviceId] = new ManagedDeviceMemberships(
            entraDevice.Id,
            [groupId],
            [administrativeUnitId])
        {
            ExtensionAttributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["extensionAttribute1"] = "Tier 0"
            }
        };
        var service = new DeviceTaggingService(
            new StubDirectoryDeviceSource([]),
            entraClient,
            new DeviceMembershipReconciliationService(membershipClient, ledger),
            ledger,
            new OptionsTaggingRuleProvider(Microsoft.Extensions.Options.Options.Create(CreateOptions(
                ("OU=Computers,DC=example,DC=com", new Dictionary<string, string>
                {
                    ["extensionAttribute1"] = "Tier 0"
                })))),
            NullLogger<DeviceTaggingService>.Instance);

        var result = await service.TagDevicesAsync(CancellationToken.None);

        Assert.Equal(1, result.RemovedFromScope);
        Assert.Equal(1, result.Updated);
        Assert.Equal(2, result.MembershipsRemoved);
        var update = Assert.Single(entraClient.Updates);
        Assert.Null(update.Attributes["extensionAttribute1"]);
        Assert.Contains(groupId, membershipClient.RemovedGroups);
        Assert.Contains(administrativeUnitId, membershipClient.RemovedAdministrativeUnits);
        Assert.Empty(ledger.Values);
    }

    [Fact]
    public async Task TagDevicesAsync_PreservesManuallyChangedAttributeWhenDeviceLeavesTargetOus()
    {
        var deviceId = Guid.NewGuid();
        var entraDevice = CreateEntraDevice(
            deviceId,
            "ServerAd",
            ("extensionAttribute1", "Manually managed"));
        var entraClient = new StubEntraDeviceClient(new Dictionary<Guid, EntraDevice>
        {
            [deviceId] = entraDevice
        });
        var ledger = new StubMembershipLedger();
        ledger.Values[deviceId] = new ManagedDeviceMemberships(entraDevice.Id, [], [])
        {
            ExtensionAttributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["extensionAttribute1"] = "Tier 0"
            }
        };
        var service = new DeviceTaggingService(
            new StubDirectoryDeviceSource([]),
            entraClient,
            new DeviceMembershipReconciliationService(new StubEntraMembershipClient(), ledger),
            ledger,
            new OptionsTaggingRuleProvider(Microsoft.Extensions.Options.Options.Create(CreateOptions(
                ("OU=Computers,DC=example,DC=com", new Dictionary<string, string>
                {
                    ["extensionAttribute1"] = "Tier 0"
                })))),
            NullLogger<DeviceTaggingService>.Instance);

        var result = await service.TagDevicesAsync(CancellationToken.None);

        Assert.Equal(1, result.RemovedFromScope);
        Assert.Equal(0, result.Updated);
        Assert.Empty(entraClient.Updates);
        Assert.Empty(ledger.Values);
    }

    private static TaggingOptions CreateOptions(
        params (string BaseDn, Dictionary<string, string> Attributes)[] rules) =>
        new()
        {
            Rules = rules.Select((rule, index) => new TaggingRuleOptions
            {
                Name = $"Rule {index + 1}",
                Description = $"Test rule {index + 1}",
                BaseDn = rule.BaseDn,
                ExtensionAttributes = rule.Attributes
            }).ToList()
        };

    private static DeviceMembershipReconciliationService CreateMembershipService(
        StubMembershipLedger ledger) =>
        new(new StubEntraMembershipClient(), ledger);

    private static DirectoryDevice CreateDirectoryDevice(Guid deviceId, string? ou = null) =>
        new(
            deviceId,
            $"Computer-{deviceId:N}",
            $"CN={deviceId:N},{ou ?? "OU=Computers,DC=example,DC=com"}");

    private static EntraDevice CreateEntraDevice(
        Guid deviceId,
        string? extensionAttribute1,
        string trustType) =>
        CreateEntraDevice(deviceId, trustType, ("extensionAttribute1", extensionAttribute1));

    private static EntraDevice CreateEntraDevice(
        Guid deviceId,
        string trustType,
        params (string Name, string? Value)[] attributes) =>
        new(
            deviceId.ToString("D"),
            deviceId,
            $"Computer-{deviceId:N}",
            trustType,
            attributes.ToDictionary(
                attribute => attribute.Name,
                attribute => attribute.Value,
                StringComparer.OrdinalIgnoreCase));

    private sealed class StubDirectoryDeviceSource : IActiveDirectoryDeviceSource
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<DirectoryDevice>> _devicesByBaseDn;

        public StubDirectoryDeviceSource(IReadOnlyList<DirectoryDevice> devices)
        {
            _devicesByBaseDn = new Dictionary<string, IReadOnlyList<DirectoryDevice>>
            {
                ["OU=Computers,DC=example,DC=com"] = devices
            };
        }

        public StubDirectoryDeviceSource(
            IReadOnlyDictionary<string, IReadOnlyList<DirectoryDevice>> devicesByBaseDn) =>
            _devicesByBaseDn = devicesByBaseDn;

        public List<string> RequestedBaseDns { get; } = [];

        public Task<IReadOnlyList<DirectoryDevice>> GetHybridJoinedDevicesAsync(
            string baseDn,
            CancellationToken cancellationToken)
        {
            RequestedBaseDns.Add(baseDn);
            return Task.FromResult(_devicesByBaseDn.TryGetValue(baseDn, out var devices)
                ? devices
                : (IReadOnlyList<DirectoryDevice>)[]);
        }
    }

    private sealed class StubEntraDeviceClient : IEntraDeviceClient
    {
        private readonly IReadOnlyDictionary<Guid, EntraDevice> _devices;

        public StubEntraDeviceClient(IReadOnlyDictionary<Guid, EntraDevice> devices)
        {
            _devices = devices;
        }

        public List<(string ObjectId, IReadOnlyDictionary<string, string?> Attributes)> Updates { get; } = [];

        public Task<EntraDevice?> FindByDeviceIdAsync(
            Guid deviceId,
            CancellationToken cancellationToken)
        {
            _devices.TryGetValue(deviceId, out var device);
            return Task.FromResult(device);
        }

        public Task SetExtensionAttributesAsync(
            string entraObjectId,
            IReadOnlyDictionary<string, string?> extensionAttributes,
            CancellationToken cancellationToken)
        {
            Updates.Add((entraObjectId, extensionAttributes));
            return Task.CompletedTask;
        }
    }

    private sealed class StubEntraMembershipClient : IEntraMembershipClient
    {
        public HashSet<Guid> GroupMemberships { get; } = [];
        public HashSet<Guid> AdministrativeUnitMemberships { get; } = [];
        public List<Guid> RemovedGroups { get; } = [];
        public List<Guid> RemovedAdministrativeUnits { get; } = [];

        public Task<bool> IsGroupMemberAsync(
            Guid groupId,
            string entraObjectId,
            CancellationToken cancellationToken) =>
            Task.FromResult(GroupMemberships.Contains(groupId));

        public Task AddGroupMemberAsync(
            Guid groupId,
            string entraObjectId,
            CancellationToken cancellationToken)
        {
            GroupMemberships.Add(groupId);
            return Task.CompletedTask;
        }

        public Task RemoveGroupMemberAsync(
            Guid groupId,
            string entraObjectId,
            CancellationToken cancellationToken)
        {
            GroupMemberships.Remove(groupId);
            RemovedGroups.Add(groupId);
            return Task.CompletedTask;
        }

        public Task<bool> IsAdministrativeUnitMemberAsync(
            Guid administrativeUnitId,
            string entraObjectId,
            CancellationToken cancellationToken) =>
            Task.FromResult(AdministrativeUnitMemberships.Contains(administrativeUnitId));

        public Task AddAdministrativeUnitMemberAsync(
            Guid administrativeUnitId,
            string entraObjectId,
            CancellationToken cancellationToken)
        {
            AdministrativeUnitMemberships.Add(administrativeUnitId);
            return Task.CompletedTask;
        }

        public Task RemoveAdministrativeUnitMemberAsync(
            Guid administrativeUnitId,
            string entraObjectId,
            CancellationToken cancellationToken)
        {
            AdministrativeUnitMemberships.Remove(administrativeUnitId);
            RemovedAdministrativeUnits.Add(administrativeUnitId);
            return Task.CompletedTask;
        }
    }

    private sealed class StubMembershipLedger : IDeviceMembershipLedger
    {
        public Dictionary<Guid, ManagedDeviceMemberships> Values { get; } = [];

        public Task<IReadOnlyDictionary<Guid, ManagedDeviceMemberships>> GetAllAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ManagedDeviceMemberships>>(
                new Dictionary<Guid, ManagedDeviceMemberships>(Values));

        public Task<ManagedDeviceMemberships?> GetAsync(
            Guid deviceId,
            CancellationToken cancellationToken)
        {
            Values.TryGetValue(deviceId, out var memberships);
            return Task.FromResult(memberships);
        }

        public Task SetAsync(
            Guid deviceId,
            ManagedDeviceMemberships memberships,
            CancellationToken cancellationToken)
        {
            if (memberships.ExtensionAttributes.Count == 0 &&
                memberships.EntraGroupIds.Count == 0 &&
                memberships.AdministrativeUnitIds.Count == 0)
            {
                Values.Remove(deviceId);
            }
            else
            {
                Values[deviceId] = memberships;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRunTraceSession : IRunTraceSession
    {
        public List<RecordedTraceEvent> Events { get; } = [];

        public string SerializedDetails =>
            System.Text.Json.JsonSerializer.Serialize(Events.Select(traceEvent => traceEvent.Details));

        public Task WriteAsync(
            string level,
            string phase,
            string action,
            string outcome,
            IReadOnlyDictionary<string, object?>? details,
            Exception? exception,
            CancellationToken cancellationToken)
        {
            Events.Add(new RecordedTraceEvent(phase, action, outcome, details));
            return Task.CompletedTask;
        }
    }

    private sealed record RecordedTraceEvent(
        string Phase,
        string Action,
        string Outcome,
        IReadOnlyDictionary<string, object?>? Details);
}