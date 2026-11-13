/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using HybridDeviceTagger.Options;

namespace HybridDeviceTagger.Services;

/// <summary>Applies validated atomic tagging-rule mutations.</summary>
public sealed class TaggingConfigurationService
{
    /// <summary>Web-default JSON options used to measure the serialized configuration size.</summary>
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    /// <summary>The versioned rule store.</summary>
    private readonly ITaggingConfigurationStore _store;

    /// <summary>Initializes the configuration service.</summary>
    /// <param name="store">The versioned rule store.</param>
    public TaggingConfigurationService(ITaggingConfigurationStore store)
    {
        _store = store;
    }

    /// <summary>Gets the current configuration.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The current snapshot.</returns>
    public Task<TaggingConfigurationSnapshot> GetAsync(CancellationToken cancellationToken) =>
        _store.GetSnapshotAsync(cancellationToken);

    /// <summary>Replaces the complete configuration.</summary>
    /// <param name="rules">The complete new rule set.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <param name="expectedETag">
    /// The ETag supplied by the caller; when omitted, the ETag of the snapshot read by this method is
    /// used, so concurrent changes between the read and the write are still detected.
    /// </param>
    /// <returns>The stored snapshot.</returns>
    /// <exception cref="TaggingConfigurationValidationException">The resulting rules are invalid.</exception>
    /// <exception cref="TaggingConfigurationConflictException">The configuration changed concurrently.</exception>
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
    /// <param name="rule">The complete rule; a replaced rule moves to the end of the ordered list.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <param name="expectedETag">
    /// The ETag supplied by the caller; when omitted, the ETag of the snapshot read by this method is
    /// used, so concurrent changes between the read and the write are still detected.
    /// </param>
    /// <returns>The stored snapshot.</returns>
    /// <exception cref="TaggingConfigurationValidationException">The resulting rules are invalid.</exception>
    /// <exception cref="TaggingConfigurationConflictException">The configuration changed concurrently.</exception>
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
    /// <param name="ruleName">The name of the rule to remove.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <param name="expectedETag">
    /// The ETag supplied by the caller; when omitted, the ETag of the snapshot read by this method is
    /// used, so concurrent changes between the read and the write are still detected.
    /// </param>
    /// <returns>The stored snapshot.</returns>
    /// <exception cref="KeyNotFoundException">No rule has the specified name.</exception>
    /// <exception cref="TaggingConfigurationValidationException">The resulting rules are invalid.</exception>
    /// <exception cref="TaggingConfigurationConflictException">The configuration changed concurrently.</exception>
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
    /// <remarks>
    /// Names, descriptions, and base DNs are trimmed, trailing commas are removed from base DNs, and
    /// collections are copied so callers cannot mutate the stored rules. All errors are collected
    /// before throwing so callers can fix every problem at once. At least one rule is required.
    /// </remarks>
    /// <param name="rules">The rules to normalize.</param>
    /// <returns>The normalized copy of the rules.</returns>
    /// <exception cref="TaggingConfigurationValidationException">One or more rules are invalid.</exception>
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