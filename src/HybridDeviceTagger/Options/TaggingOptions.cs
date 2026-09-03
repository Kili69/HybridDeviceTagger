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

    /// <summary>Gets the ordered OU tagging rules.</summary>
    [Required]
    public List<TaggingRuleOptions> Rules { get; init; } = [];

    /// <summary>Gets JSON-encoded rules used when the configuration provider cannot express arrays.</summary>
    public string? RulesJson { get; init; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
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

        for (var ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
        {
            var rule = rules[ruleIndex];
            if (string.IsNullOrWhiteSpace(rule.BaseDn))
            {
                validationResults.Add(new ValidationResult(
                    $"Tagging rule {ruleIndex} requires a BaseDn.",
                    [nameof(Rules)]));
            }
                    else if (ActiveDirectorySearchPath.IsForestRelative(rule.BaseDn) &&
                         !ActiveDirectorySearchPath.IsOuPath(rule.BaseDn))
                    {
                    validationResults.Add(new ValidationResult(
                        $"Tagging rule {ruleIndex} must be a full distinguished name or an OU-only path.",
                        [nameof(Rules)]));
                    }

            if (rule.ExtensionAttributes.Count == 0)
            {
                validationResults.Add(new ValidationResult(
                    $"Tagging rule {ruleIndex} requires at least one extension attribute.",
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
            }
        }

        return validationResults;
    }

    internal IReadOnlyList<TaggingRuleOptions> GetRules() =>
        Rules.Count > 0 || string.IsNullOrWhiteSpace(RulesJson)
            ? Rules
            : JsonSerializer.Deserialize<List<TaggingRuleOptions>>(
                RulesJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

    [GeneratedRegex("^extensionAttribute(?:[1-9]|1[0-5])$", RegexOptions.IgnoreCase)]
    private static partial Regex ExtensionAttributeName();
}

/// <summary>Maps an Active Directory OU subtree to one or more Entra extension attributes.</summary>
public sealed class TaggingRuleOptions
{
    /// <summary>Gets a full distinguished name or an OU-only path resolved in every forest domain.</summary>
    [Required]
    public string BaseDn { get; init; } = string.Empty;

    /// <summary>Gets the extension-attribute names and desired values.</summary>
    [Required]
    [MinLength(1)]
    public Dictionary<string, string> ExtensionAttributes { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}