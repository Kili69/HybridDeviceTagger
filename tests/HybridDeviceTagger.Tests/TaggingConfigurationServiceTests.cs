using HybridDeviceTagger.Options;
using HybridDeviceTagger.Services;

namespace HybridDeviceTagger.Tests;

public sealed class TaggingConfigurationServiceTests
{
    [Fact]
    public async Task UpsertAsync_ReplacesRuleByCaseInsensitiveName()
    {
        var store = new InMemoryTaggingConfigurationStore([CreateRule("Servers", "Old")]);
        var service = new TaggingConfigurationService(store);

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
        var service = new TaggingConfigurationService(store);

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
        var service = new TaggingConfigurationService(store);

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
        var service = new TaggingConfigurationService(store);

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

    private static TaggingRuleOptions CreateRule(string name, string value) => new()
    {
        Name = name,
        Description = "Test rule",
        BaseDn = "OU=Servers,DC=example,DC=com",
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
}