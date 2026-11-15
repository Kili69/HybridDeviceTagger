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
/// <remarks>
/// Every mutation reads the current snapshot, builds the complete new rule set, validates it locally
/// and against the Active Directory forest, and writes it conditionally on an ETag. Concurrent
/// changes therefore fail with <see cref="TaggingConfigurationConflictException"/> instead of being lost.
/// </remarks>
public sealed class TaggingConfigurationService
{
    /// <summary>Web JSON defaults used to measure the serialized configuration size.</summary>
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    /// <summary>Persists the tagging configuration.</summary>
    private readonly ITaggingConfigurationStore _store;

    /// <summary>Validates rule search paths against the Active Directory forest.</summary>
    private readonly IActiveDirectoryRuleValidator _activeDirectoryRuleValidator;

    /// <summary>Initializes the configuration service.</summary>
    /// <param name="store">Persists the tagging configuration.</param>
    /// <param name="activeDirectoryRuleValidator">Validates rule search paths against the Active Directory forest.</param>
    public TaggingConfigurationService(
        ITaggingConfigurationStore store,
        IActiveDirectoryRuleValidator activeDirectoryRuleValidator)
    {
        _store = store;
        _activeDirectoryRuleValidator = activeDirectoryRuleValidator;
    }

    /// <summary>Gets the current configuration.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The current configuration snapshot.</returns>
    public Task<TaggingConfigurationSnapshot> GetAsync(CancellationToken cancellationToken) =>
        _store.GetSnapshotAsync(cancellationToken);

    /// <summary>Replaces the complete configuration.</summary>
    /// <param name="rules">The complete new rule set.</param>
    /// <param name="cancellationToken">Cancels validation and storage.</param>
    /// <param name="expectedETag">The ETag the stored configuration must have; the current ETag is used when omitted.</param>
    /// <param name="allowNewMembershipTargets">
    /// <see langword="false"/> rejects group or administrative-unit IDs that no stored rule uses yet.
    /// </param>
    /// <returns>The stored configuration snapshot.</returns>
    /// <exception cref="TaggingConfigurationValidationException">The rules are invalid.</exception>
    /// <exception cref="TaggingConfigurationConflictException">The stored configuration changed concurrently.</exception>
    public async Task<TaggingConfigurationSnapshot> ReplaceAsync(
        IReadOnlyList<TaggingRuleOptions> rules,
        CancellationToken cancellationToken,
        string? expectedETag = null,
        bool allowNewMembershipTargets = true)
    {
        var current = await _store.GetSnapshotAsync(cancellationToken);
        var normalizedRules = NormalizeAndValidate(rules);
        EnsureNoNewMembershipTargets(current.Rules, normalizedRules, allowNewMembershipTargets);
        await _activeDirectoryRuleValidator.ValidateRulesAsync(
            normalizedRules,
            cancellationToken);
        return await _store.ReplaceAsync(
            normalizedRules,
            expectedETag ?? current.ETag,
            cancellationToken);
    }

    /// <summary>Adds a rule or replaces the rule with the same case-insensitive name.</summary>
    /// <param name="rule">The rule to add or replace.</param>
    /// <param name="cancellationToken">Cancels validation and storage.</param>
    /// <param name="expectedETag">The ETag the stored configuration must have; the current ETag is used when omitted.</param>
    /// <param name="allowNewMembershipTargets">
    /// <see langword="false"/> rejects group or administrative-unit IDs that no stored rule uses yet.
    /// </param>
    /// <returns>The stored configuration snapshot.</returns>
    /// <exception cref="TaggingConfigurationValidationException">The resulting rule set is invalid.</exception>
    /// <exception cref="TaggingConfigurationConflictException">The stored configuration changed concurrently.</exception>
    public async Task<TaggingConfigurationSnapshot> UpsertAsync(
        TaggingRuleOptions rule,
        CancellationToken cancellationToken,
        string? expectedETag = null,
        bool allowNewMembershipTargets = true)
    {
        var current = await _store.GetSnapshotAsync(cancellationToken);
        var rules = current.Rules
            .Where(existing => !string.Equals(existing.Name, rule.Name, StringComparison.OrdinalIgnoreCase))
            .Append(rule)
            .ToArray();
        var normalizedRules = NormalizeAndValidate(rules);
        EnsureNoNewMembershipTargets(current.Rules, normalizedRules, allowNewMembershipTargets);
        await _activeDirectoryRuleValidator.ValidateRulesAsync(
            normalizedRules,
            cancellationToken);
        return await _store.ReplaceAsync(
            normalizedRules,
            expectedETag ?? current.ETag,
            cancellationToken);
    }

    /// <summary>Removes one rule by its case-insensitive name.</summary>
    /// <param name="ruleName">The name of the rule to remove.</param>
    /// <param name="cancellationToken">Cancels validation and storage.</param>
    /// <param name="expectedETag">The ETag the stored configuration must have; the current ETag is used when omitted.</param>
    /// <returns>The stored configuration snapshot.</returns>
    /// <exception cref="KeyNotFoundException">No rule has the specified name.</exception>
    /// <exception cref="TaggingConfigurationValidationException">The remaining rule set is invalid, for example empty.</exception>
    /// <exception cref="TaggingConfigurationConflictException">The stored configuration changed concurrently.</exception>
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

    /// <summary>Rejects group or administrative-unit targets that no stored rule uses yet.</summary>
    /// <remarks>
    /// The Function identity can change memberships of every group and administrative unit in the
    /// tenant. Principals without Function App Owner/Contributor access may therefore only reuse
    /// targets that such an administrator has already approved in a rule.
    /// </remarks>
    /// <param name="currentRules">The stored rules.</param>
    /// <param name="newRules">The normalized new rules.</param>
    /// <param name="allowNewMembershipTargets">Whether new targets are allowed.</param>
    /// <exception cref="TaggingConfigurationValidationException">A new target was added without permission.</exception>
    internal static void EnsureNoNewMembershipTargets(
        IReadOnlyList<TaggingRuleOptions> currentRules,
        IReadOnlyList<TaggingRuleOptions> newRules,
        bool allowNewMembershipTargets)
    {
        if (allowNewMembershipTargets)
        {
            return;
        }

        var approved = currentRules.SelectMany(rule => rule.EntraGroupIds.Concat(rule.AdministrativeUnitIds)).ToHashSet();
        var added = newRules.SelectMany(rule => rule.EntraGroupIds.Concat(rule.AdministrativeUnitIds))
            .Where(id => !approved.Contains(id))
            .Distinct()
            .ToArray();
        if (added.Length > 0)
        {
            throw new TaggingConfigurationValidationException(
            [
                "Only Function App Owners or Contributors can add new Entra group or administrative-unit targets: " +
                string.Join(", ", added)
            ]);
        }
    }

    /// <summary>Normalizes and validates a complete tagging-rule collection.</summary>
    /// <remarks>
    /// Text values are trimmed, trailing commas are removed from <c>BaseDn</c>, null collections become
    /// empty, and extension-attribute names become case-insensitive. All errors are collected
    /// before a single exception is thrown.
    /// </remarks>
    /// <param name="rules">The rules to normalize.</param>
    /// <returns>The normalized rules.</returns>
    /// <exception cref="TaggingConfigurationValidationException">
    /// The collection is empty, contains null or invalid rules, or exceeds the serialized size limit.
    /// </exception>
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