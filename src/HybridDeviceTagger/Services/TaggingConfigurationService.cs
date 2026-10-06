/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * Copyright 2026 Andreas Lucas
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 * SPDX-License-Identifier: Apache-2.0
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
    private readonly IActiveDirectoryRuleValidator _activeDirectoryRuleValidator;

    /// <summary>Initializes the configuration service.</summary>
    public TaggingConfigurationService(
        ITaggingConfigurationStore store,
        IActiveDirectoryRuleValidator activeDirectoryRuleValidator)
    {
        _store = store;
        _activeDirectoryRuleValidator = activeDirectoryRuleValidator;
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
        await _activeDirectoryRuleValidator.ValidateRulesAsync(
            normalizedRules,
            cancellationToken);
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
        var normalizedRules = NormalizeAndValidate(rules);
        await _activeDirectoryRuleValidator.ValidateRulesAsync(
            normalizedRules,
            cancellationToken);
        return await _store.ReplaceAsync(
            normalizedRules,
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

        var normalizedRules = NormalizeAndValidate(rules);
        await _activeDirectoryRuleValidator.ValidateRulesAsync(
            normalizedRules,
            cancellationToken);
        return await _store.ReplaceAsync(
            normalizedRules,
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