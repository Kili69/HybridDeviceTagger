using HybridDeviceTagger.Options;
using HybridDeviceTagger.Services;

namespace HybridDeviceTagger.Tests;

public sealed class TaggingConfigurationServiceTests
{
    [Fact]
    public async Task UpsertAsync_AddsFirstRuleToEmptyConfiguration()
    {
        var store = new InMemoryTaggingConfigurationStore([]);
        var service = CreateService(store);

        var result = await service.UpsertAsync(
            CreateRule("Servers", "Managed"),
            CancellationToken.None);

        var rule = Assert.Single(result.Rules);
        Assert.Equal("Servers", rule.Name);
        Assert.Equal("Managed", rule.ExtensionAttributes["extensionAttribute1"]);
        Assert.Equal("1", store.LastExpectedETag);
    }

    [Fact]
    public async Task UpsertAsync_ReplacesRuleByCaseInsensitiveName()
    {
        var store = new InMemoryTaggingConfigurationStore([CreateRule("Servers", "Old")]);
        var service = CreateService(store);

        var result = await service.UpsertAsync(
            CreateRule("servers", "New"),
            CancellationToken.None);

        var rule = Assert.Single(result.Rules);
        Assert.Equal("servers", rule.Name);
        Assert.Equal("New", rule.ExtensionAttributes["extensionAttribute1"]);
        Assert.Equal("2", result.ETag);
    }

    [Fact]
    public async Task ReplaceAsync_RejectsInvalidRuleWithoutWriting()
    {
        var store = new InMemoryTaggingConfigurationStore([CreateRule("Servers", "Old")]);
        var service = CreateService(store);

        var exception = await Assert.ThrowsAsync<TaggingConfigurationValidationException>(() =>
            service.ReplaceAsync(
                [new TaggingRuleOptions { Name = "Invalid" }],
                CancellationToken.None));

        Assert.Contains(exception.Errors, error => error.Contains("Description", StringComparison.Ordinal));
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task ReplaceAsync_UsesCallerETag()
    {
        var store = new InMemoryTaggingConfigurationStore([CreateRule("Servers", "Old")]);
        var service = CreateService(store);

        await service.ReplaceAsync(
            [CreateRule("Servers", "New")],
            CancellationToken.None,
            "client-etag");

        Assert.Equal("client-etag", store.LastExpectedETag);
    }

    [Fact]
    public async Task RemoveAsync_RejectsRemovingTheOnlyRule()
    {
        var store = new InMemoryTaggingConfigurationStore([CreateRule("Servers", "Old")]);
        var service = CreateService(store);

        var exception = await Assert.ThrowsAsync<TaggingConfigurationValidationException>(() =>
            service.RemoveAsync("servers", CancellationToken.None));

        Assert.Contains(exception.Errors, error => error.Contains("At least one", StringComparison.Ordinal));
    }

    [Fact]
    public void NormalizeAndValidate_ReportsExplicitNullFields()
    {
        var rule = new TaggingRuleOptions
        {
            Name = null!,
            Description = null!,
            BaseDn = null!,
            ExtensionAttributes = null!,
            EntraGroupIds = null!,
            AdministrativeUnitIds = null!
        };

        var exception = Assert.Throws<TaggingConfigurationValidationException>(() =>
            TaggingConfigurationService.NormalizeAndValidate([rule]));

        Assert.NotEmpty(exception.Errors);
    }

    [Fact]
    public async Task ReplaceAsync_ValidatesForestDomainsBeforeWriting()
    {
        var store = new InMemoryTaggingConfigurationStore([CreateRule("Servers", "Old")]);
        var validator = new RecordingRuleValidator(rejectRules: true);
        var service = CreateService(store, validator);

        await Assert.ThrowsAsync<TaggingConfigurationValidationException>(() =>
            service.ReplaceAsync(
                [CreateRule("External", "Blocked")],
                CancellationToken.None));

        Assert.Equal(1, validator.CallCount);
        Assert.Equal(0, store.WriteCount);
    }

    private static TaggingConfigurationService CreateService(
        ITaggingConfigurationStore store,
        IActiveDirectoryRuleValidator? validator = null) =>
        new(store, validator ?? new RecordingRuleValidator());

    [Fact]
    public async Task UpsertAsync_RejectsNewMembershipTargetsWithoutPermission()
    {
        var approvedGroup = Guid.NewGuid();
        var store = new InMemoryTaggingConfigurationStore(
            [CreateRule("Servers", "Old", groupIds: [approvedGroup])]);
        var service = CreateService(store);
        var newGroup = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<TaggingConfigurationValidationException>(() =>
            service.UpsertAsync(
                CreateRule("Clients", "New", groupIds: [approvedGroup, newGroup]),
                CancellationToken.None,
                allowNewMembershipTargets: false));

        Assert.Contains(exception.Errors, error => error.Contains(newGroup.ToString(), StringComparison.Ordinal));
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task UpsertAsync_AllowsApprovedTargetsWithoutPermissionAndNewTargetsWithPermission()
    {
        var approvedGroup = Guid.NewGuid();
        var store = new InMemoryTaggingConfigurationStore(
            [CreateRule("Servers", "Old", groupIds: [approvedGroup])]);
        var service = CreateService(store);

        await service.UpsertAsync(
            CreateRule("Servers", "New", groupIds: [approvedGroup]),
            CancellationToken.None,
            allowNewMembershipTargets: false);
        var result = await service.UpsertAsync(
            CreateRule("Clients", "New", administrativeUnitIds: [Guid.NewGuid()]),
            CancellationToken.None);

        Assert.Equal(2, result.Rules.Count);
    }

    private static TaggingRuleOptions CreateRule(
        string name,
        string value,
        List<Guid>? groupIds = null,
        List<Guid>? administrativeUnitIds = null) => new()
    {
        Name = name,
        Description = "Test rule",
        BaseDn = "OU=Servers,DC=example,DC=com",
        EntraGroupIds = groupIds ?? [],
        AdministrativeUnitIds = administrativeUnitIds ?? [],
        ExtensionAttributes = new Dictionary<string, string>
        {
            ["extensionAttribute1"] = value
        }
    };

    private sealed class InMemoryTaggingConfigurationStore : ITaggingConfigurationStore
    {
        private TaggingConfigurationSnapshot _snapshot;

        public InMemoryTaggingConfigurationStore(IReadOnlyList<TaggingRuleOptions> rules)
        {
            _snapshot = new TaggingConfigurationSnapshot(rules, "1", DateTimeOffset.UtcNow, "test");
        }

        public int WriteCount { get; private set; }
        public string? LastExpectedETag { get; private set; }

        public Task<IReadOnlyList<TaggingRuleOptions>> GetRulesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_snapshot.Rules);

        public Task<TaggingConfigurationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_snapshot);

        public Task<TaggingConfigurationSnapshot> ReplaceAsync(
            IReadOnlyList<TaggingRuleOptions> rules,
            string? expectedETag,
            CancellationToken cancellationToken)
        {
            LastExpectedETag = expectedETag;
            WriteCount++;
            _snapshot = new TaggingConfigurationSnapshot(rules, "2", DateTimeOffset.UtcNow, "test");
            return Task.FromResult(_snapshot);
        }
    }

    private sealed class RecordingRuleValidator(bool rejectRules = false)
        : IActiveDirectoryRuleValidator
    {
        public int CallCount { get; private set; }

        public Task ValidateRulesAsync(
            IReadOnlyList<TaggingRuleOptions> rules,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (rejectRules)
            {
                throw new TaggingConfigurationValidationException(
                    ["The rule targets a domain outside the configured forest."]);
            }

            return Task.CompletedTask;
        }
    }
}