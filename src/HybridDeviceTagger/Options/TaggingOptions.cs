using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;
using HybridDeviceTagger.Services;

namespace HybridDeviceTagger.Options;

/// <summary>Configures OU-based Microsoft Entra device tagging rules.</summary>
public sealed partial class TaggingOptions : IValidatableObject
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "Tagging";
    internal const int MaximumRuleCount = 100;
    internal const int MaximumRulesJsonLength = 32768;
    internal const int MaximumRuleDescriptionLength = 1024;
    internal const int MaximumBaseDnLength = 2048;
    internal const int MaximumExtensionAttributeValueLength = 1024;
    internal const int MaximumMembershipTargetCount = 50;

    /// <summary>Gets the ordered OU tagging rules.</summary>
    [Required]
    public List<TaggingRuleOptions> Rules { get; init; } = [];

    /// <summary>Gets JSON-encoded rules used when the configuration provider cannot express arrays.</summary>
    /// <remarks>Structured <see cref="Rules"/> take precedence when both forms are supplied.</remarks>
    public string? RulesJson { get; init; }

    /// <inheritdoc />
    /// <remarks>
    /// Validation accepts full domain-specific distinguished names or forest-relative OU-only
    /// paths and restricts assignments to non-empty <c>extensionAttribute1</c> through
    /// <c>extensionAttribute15</c> values.
    /// </remarks>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RulesJson?.Length > MaximumRulesJsonLength)
        {
            return
            [
                new ValidationResult(
                    $"Tagging RulesJson cannot exceed {MaximumRulesJsonLength} characters.",
                    [nameof(RulesJson)])
            ];
        }

        IReadOnlyList<TaggingRuleOptions> rules;
        try
        {
            rules = GetRules();
        }
        catch (JsonException exception)
        {
            return
            [
                new ValidationResult(
                    $"Tagging RulesJson is invalid: {exception.Message}",
                    [nameof(RulesJson)])
            ];
        }

        var validationResults = new List<ValidationResult>();
        if (rules.Count == 0)
        {
            validationResults.Add(new ValidationResult(
                "At least one tagging rule is required.",
                [nameof(Rules), nameof(RulesJson)]));
        }
        else if (rules.Count > MaximumRuleCount)
        {
            return
            [
                new ValidationResult(
                $"Tagging configuration cannot contain more than {MaximumRuleCount} rules.",
                [nameof(Rules), nameof(RulesJson)])
            ];
        }

        var ruleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
        {
            var rule = rules[ruleIndex];
            if (string.IsNullOrWhiteSpace(rule.Name))
            {
                validationResults.Add(new ValidationResult(
                    $"Tagging rule {ruleIndex} requires a Name.",
                    [nameof(Rules)]));
            }
            else if (!RuleName().IsMatch(rule.Name.Trim()))
            {
                validationResults.Add(new ValidationResult(
                    $"Tagging rule name '{rule.Name}' contains unsupported characters.",
                    [nameof(Rules)]));
            }
            else if (!ruleNames.Add(rule.Name.Trim()))
            {
                validationResults.Add(new ValidationResult(
                    $"Tagging rule name '{rule.Name}' is duplicated.",
                    [nameof(Rules)]));
            }

            if (string.IsNullOrWhiteSpace(rule.Description))
            {
                validationResults.Add(new ValidationResult(
                    $"Tagging rule {ruleIndex} requires a Description.",
                    [nameof(Rules)]));
            }
            else if (rule.Description.Length > MaximumRuleDescriptionLength)
            {
                validationResults.Add(new ValidationResult(
                        $"Tagging rule '{rule.Name}' Description cannot exceed {MaximumRuleDescriptionLength} characters.",
                        [nameof(Rules)]));
            }

            if (string.IsNullOrWhiteSpace(rule.BaseDn))
            {
                validationResults.Add(new ValidationResult(
                    $"Tagging rule {ruleIndex} requires a BaseDn.",
                    [nameof(Rules)]));
            }
            else if (rule.BaseDn.Length > MaximumBaseDnLength)
            {
                validationResults.Add(new ValidationResult(
                        $"Tagging rule '{rule.Name}' BaseDn cannot exceed {MaximumBaseDnLength} characters.",
                        [nameof(Rules)]));
            }
            else if (!ActiveDirectorySearchPath.IsValidBaseDn(rule.BaseDn))
            {
                validationResults.Add(new ValidationResult(
                    $"Tagging rule {ruleIndex} must be a full distinguished name or an OU-only path.",
                    [nameof(Rules)]));
            }

            if (rule.ExtensionAttributes.Count == 0 &&
                rule.EntraGroupIds.Count == 0 &&
                rule.AdministrativeUnitIds.Count == 0)
            {
                validationResults.Add(new ValidationResult(
                    $"Tagging rule {ruleIndex} requires at least one extension attribute, " +
                    "Entra group, or administrative unit.",
                    [nameof(Rules)]));
            }
            if (rule.ExtensionAttributes.Count > 15)
            {
                validationResults.Add(new ValidationResult(
                        $"Tagging rule '{rule.Name}' cannot contain more than 15 extension attributes.",
                        [nameof(Rules)]));
            }

            foreach (var (attributeName, value) in rule.ExtensionAttributes)
            {
                if (!ExtensionAttributeName().IsMatch(attributeName))
                {
                    validationResults.Add(new ValidationResult(
                        $"Tagging rule {ruleIndex} contains unsupported attribute '{attributeName}'. " +
                        "Only extensionAttribute1 through extensionAttribute15 are supported.",
                        [nameof(Rules)]));
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    validationResults.Add(new ValidationResult(
                        $"Tagging rule {ruleIndex} requires a non-empty value for '{attributeName}'.",
                        [nameof(Rules)]));
                }
                else if (value.Length > MaximumExtensionAttributeValueLength)
                {
                    validationResults.Add(new ValidationResult(
                        $"Tagging rule '{rule.Name}' value for '{attributeName}' cannot exceed " +
                        $"{MaximumExtensionAttributeValueLength} characters.",
                        [nameof(Rules)]));
                }
            }

            ValidateMembershipTargets(
                rule.EntraGroupIds,
                "Entra group",
                rule.Name,
                validationResults);
            ValidateMembershipTargets(
                rule.AdministrativeUnitIds,
                "administrative unit",
                rule.Name,
                validationResults);
        }

        return validationResults;
    }

    private static void ValidateMembershipTargets(
        IReadOnlyCollection<Guid> targetIds,
        string targetType,
        string ruleName,
        ICollection<ValidationResult> validationResults)
    {
        if (targetIds.Count > MaximumMembershipTargetCount)
        {
            validationResults.Add(new ValidationResult(
                $"Tagging rule '{ruleName}' cannot contain more than " +
                $"{MaximumMembershipTargetCount} {targetType} IDs.",
                [nameof(Rules)]));
        }

        if (targetIds.Any(id => id == Guid.Empty))
        {
            validationResults.Add(new ValidationResult(
                $"Tagging rule '{ruleName}' contains an empty {targetType} ID.",
                [nameof(Rules)]));
        }

        if (targetIds.Distinct().Count() != targetIds.Count)
        {
            validationResults.Add(new ValidationResult(
                $"Tagging rule '{ruleName}' contains duplicate {targetType} IDs.",
                [nameof(Rules)]));
        }
    }

    /// <summary>Returns structured rules or deserializes the JSON fallback when needed.</summary>
    /// <returns>The effective ordered rule collection.</returns>
    /// <exception cref="JsonException"><see cref="RulesJson"/> is not valid rule JSON.</exception>
    internal IReadOnlyList<TaggingRuleOptions> GetRules() =>
        Rules.Count > 0 || string.IsNullOrWhiteSpace(RulesJson)
            ? Rules
            : JsonSerializer.Deserialize<List<TaggingRuleOptions>>(
                RulesJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

    [GeneratedRegex("^extensionAttribute(?:[1-9]|1[0-5])$", RegexOptions.IgnoreCase)]
    private static partial Regex ExtensionAttributeName();

    [GeneratedRegex(@"^[\p{L}\p{N}][\p{L}\p{N} ._()\-]{0,127}$")]
    private static partial Regex RuleName();
}

/// <summary>Maps an Active Directory OU subtree to one or more Entra extension attributes.</summary>
public sealed class TaggingRuleOptions
{
    /// <summary>Gets the unique operational name used to identify this rule.</summary>
    [Required]
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the human-readable purpose of this rule.</summary>
    [Required]
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets a full distinguished name or an OU-only path resolved in every forest domain.</summary>
    [Required]
    public string BaseDn { get; init; } = string.Empty;

    /// <summary>Gets the extension-attribute names and desired values.</summary>
    [Required]
    public Dictionary<string, string> ExtensionAttributes { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets Entra security-group object IDs whose membership is managed by this rule.</summary>
    public List<Guid> EntraGroupIds { get; init; } = [];

    /// <summary>Gets normal or restricted administrative-unit object IDs managed by this rule.</summary>
    public List<Guid> AdministrativeUnitIds { get; init; } = [];
}