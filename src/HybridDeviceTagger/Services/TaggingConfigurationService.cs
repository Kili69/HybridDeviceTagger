/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using HybridDeviceTagger.Options;

namespace HybridDeviceTagger.Services;

/// <summary>Applies validated atomic tagging-rule mutations.</summary>
public sealed class TaggingConfigurationService
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly ITaggingConfigurationStore _store;

    /// <summary>Initializes the configuration service.</summary>
    public TaggingConfigurationService(ITaggingConfigurationStore store)
    {
        _store = store;
    }

    /// <summary>Gets the current configuration.</summary>
    public Task<TaggingConfigurationSnapshot> GetAsync(CancellationToken cancellationToken) =>
        _store.GetSnapshotAsync(cancellationToken);

    /// <summary>Replaces the complete configuration.</summary>
    public async Task<TaggingConfigurationSnapshot> ReplaceAsync(
        IReadOnlyList<TaggingRuleOptions> rules,
        CancellationToken cancellationToken,
        string? expectedETag = null)
    {
        var current = await _store.GetSnapshotAsync(cancellationToken);
        var normalizedRules = NormalizeAndValidate(rules);
        return await _store.ReplaceAsync(
            normalizedRules,
            expectedETag ?? current.ETag,
            cancellationToken);
    }

    /// <summary>Adds a rule or replaces the rule with the same case-insensitive name.</summary>
    public async Task<TaggingConfigurationSnapshot> UpsertAsync(
        TaggingRuleOptions rule,
        CancellationToken cancellationToken,
        string? expectedETag = null)
    {
        var current = await _store.GetSnapshotAsync(cancellationToken);
        var rules = current.Rules
            .Where(existing => !string.Equals(existing.Name, rule.Name, StringComparison.OrdinalIgnoreCase))
            .Append(rule)
            .ToArray();
        return await _store.ReplaceAsync(
            NormalizeAndValidate(rules),
            expectedETag ?? current.ETag,
            cancellationToken);
    }

    /// <summary>Removes one rule by its case-insensitive name.</summary>
    public async Task<TaggingConfigurationSnapshot> RemoveAsync(
        string ruleName,
        CancellationToken cancellationToken,
        string? expectedETag = null)
    {
        var current = await _store.GetSnapshotAsync(cancellationToken);
        var rules = current.Rules
            .Where(rule => !string.Equals(rule.Name, ruleName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (rules.Length == current.Rules.Count)
        {
            throw new KeyNotFoundException($"Tagging rule '{ruleName}' was not found.");
        }

        return await _store.ReplaceAsync(
            NormalizeAndValidate(rules),
            expectedETag ?? current.ETag,
            cancellationToken);
    }

    /// <summary>Normalizes and validates a complete tagging-rule collection.</summary>
    public static IReadOnlyList<TaggingRuleOptions> NormalizeAndValidate(
        IReadOnlyList<TaggingRuleOptions> rules)
    {
        var errors = new List<string>();
        var normalized = new List<TaggingRuleOptions>();
        if (rules.Count == 0)
        {
            errors.Add("At least one tagging rule is required.");
        }

        for (var ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
        {
            var rule = rules[ruleIndex];
            if (rule is null)
            {
                errors.Add($"Tagging rule {ruleIndex} cannot be null.");
                continue;
            }

            normalized.Add(new TaggingRuleOptions
            {
                Name = (rule.Name ?? string.Empty).Trim(),
                Description = (rule.Description ?? string.Empty).Trim(),
                BaseDn = (rule.BaseDn ?? string.Empty).Trim().TrimEnd(','),
                ExtensionAttributes = new Dictionary<string, string>(
                    rule.ExtensionAttributes ?? [],
                    StringComparer.OrdinalIgnoreCase),
                EntraGroupIds = [.. rule.EntraGroupIds ?? []],
                AdministrativeUnitIds = [.. rule.AdministrativeUnitIds ?? []]
            });
        }
        var options = new TaggingOptions { Rules = normalized };
        errors.AddRange(options.Validate(new ValidationContext(options))
            .Select(result => result.ErrorMessage ?? "Unknown tagging-rule validation error.")
            .ToList());
        if (JsonSerializer.Serialize(normalized, SerializerOptions).Length >
            TaggingOptions.MaximumRulesJsonLength)
        {
            errors.Add(
                $"Serialized tagging configuration cannot exceed " +
                $"{TaggingOptions.MaximumRulesJsonLength} characters.");
        }

        if (errors.Count > 0)
        {
            throw new TaggingConfigurationValidationException(errors);
        }

        return normalized;
    }
}