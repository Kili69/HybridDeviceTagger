using System.Text.Json;
using HybridDeviceTagger.Models;
using HybridDeviceTagger.Services;

namespace HybridDeviceTagger.Tests;

public sealed class DeviceMembershipReconciliationServiceTests
{
    private static readonly Guid GroupId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AdministrativeUnitId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void ManagedDeviceMemberships_DeserializesLegacyMembershipOnlyState()
    {
        var state = JsonSerializer.Deserialize<ManagedDeviceMemberships>(
            """
            {
              "entraObjectId": "entra-device-id",
              "entraGroupIds": ["11111111-1111-1111-1111-111111111111"],
              "administrativeUnitIds": ["22222222-2222-2222-2222-222222222222"]
            }
            """,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(state);
        Assert.Empty(state.ExtensionAttributes);
        Assert.Contains(GroupId, state.EntraGroupIds);
        Assert.Contains(AdministrativeUnitId, state.AdministrativeUnitIds);
    }

    [Fact]
    public async Task ReconcileAsync_AddsAndTracksMissingMemberships()
    {
        var deviceId = Guid.NewGuid();
        var client = new StubMembershipClient();
        var ledger = new StubLedger();
        var service = new DeviceMembershipReconciliationService(client, ledger);
        var trace = new RecordingRunTraceSession();

        var result = await service.ReconcileAsync(
            deviceId,
            "entra-device-id",
            new HashSet<Guid> { GroupId },
            new HashSet<Guid> { AdministrativeUnitId },
            CancellationToken.None,
            trace);

        Assert.Equal(new MembershipReconciliationResult(2, 0), result);
        Assert.Contains(GroupId, client.GroupMemberships);
        Assert.Contains(AdministrativeUnitId, client.AdministrativeUnitMemberships);
        Assert.Contains(GroupId, ledger.Value!.EntraGroupIds);
        Assert.Contains(AdministrativeUnitId, ledger.Value.AdministrativeUnitIds);
        Assert.Contains(trace.Events, traceEvent =>
            traceEvent.Action == "CheckMembership" &&
            traceEvent.Outcome == "Started" &&
            Equals(traceEvent.Details?["targetId"], GroupId));
        Assert.Contains(trace.Events, traceEvent =>
            traceEvent.Action == "AddMembership" &&
            traceEvent.Outcome == "Succeeded" &&
            Equals(traceEvent.Details?["targetId"], AdministrativeUnitId));
    }

    [Fact]
    public async Task ReconcileAsync_DoesNotClaimPreexistingManualMembership()
    {
        var deviceId = Guid.NewGuid();
        var client = new StubMembershipClient
        {
            GroupMemberships = { GroupId }
        };
        var ledger = new StubLedger();
        var service = new DeviceMembershipReconciliationService(client, ledger);

        var result = await service.ReconcileAsync(
            deviceId,
            "entra-device-id",
            new HashSet<Guid> { GroupId },
            new HashSet<Guid>(),
            CancellationToken.None);

        Assert.Equal(new MembershipReconciliationResult(0, 0), result);
        Assert.Empty(client.AddedGroups);
        Assert.Null(ledger.Value);
    }

    [Fact]
    public async Task ReconcileAsync_RemovesOnlyOwnedMembershipsNoLongerDesired()
    {
        var deviceId = Guid.NewGuid();
        var client = new StubMembershipClient
        {
            GroupMemberships = { GroupId },
            AdministrativeUnitMemberships = { AdministrativeUnitId }
        };
        var ledger = new StubLedger
        {
            Value = new ManagedDeviceMemberships(
                "entra-device-id",
                [GroupId],
                [AdministrativeUnitId])
        };
        var service = new DeviceMembershipReconciliationService(client, ledger);

        var result = await service.ReconcileAsync(
            deviceId,
            "entra-device-id",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            CancellationToken.None);

        Assert.Equal(new MembershipReconciliationResult(0, 2), result);
        Assert.Contains(GroupId, client.RemovedGroups);
        Assert.Contains(AdministrativeUnitId, client.RemovedAdministrativeUnits);
        Assert.Empty(ledger.Value!.EntraGroupIds);
        Assert.Empty(ledger.Value.AdministrativeUnitIds);
    }

    private sealed class StubMembershipClient : IEntraMembershipClient
    {
        public HashSet<Guid> GroupMemberships { get; } = [];
        public HashSet<Guid> AdministrativeUnitMemberships { get; } = [];
        public List<Guid> AddedGroups { get; } = [];
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
            AddedGroups.Add(groupId);
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

    private sealed class StubLedger : IDeviceMembershipLedger
    {
        public ManagedDeviceMemberships? Value { get; set; }

        public Task<IReadOnlyDictionary<Guid, ManagedDeviceMemberships>> GetAllAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ManagedDeviceMemberships>>(
                new Dictionary<Guid, ManagedDeviceMemberships>());

        public Task<ManagedDeviceMemberships?> GetAsync(
            Guid deviceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Value);

        public Task SetAsync(
            Guid deviceId,
            ManagedDeviceMemberships memberships,
            CancellationToken cancellationToken)
        {
            Value = memberships;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRunTraceSession : IRunTraceSession
    {
        public List<RecordedTraceEvent> Events { get; } = [];

        public Task WriteAsync(
            string level,
            string phase,
            string action,
            string outcome,
            IReadOnlyDictionary<string, object?>? details,
            Exception? exception,
            CancellationToken cancellationToken)
        {
            Events.Add(new RecordedTraceEvent(action, outcome, details));
            return Task.CompletedTask;
        }
    }

    private sealed record RecordedTraceEvent(
        string Action,
        string Outcome,
        IReadOnlyDictionary<string, object?>? Details);
}
