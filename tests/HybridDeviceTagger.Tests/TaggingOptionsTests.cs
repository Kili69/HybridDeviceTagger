using System.ComponentModel.DataAnnotations;
using HybridDeviceTagger.Options;

namespace HybridDeviceTagger.Tests;

public sealed class TaggingOptionsTests
{
    [Fact]
    public void Validate_AcceptsEmptyInitialConfiguration()
    {
        var options = new TaggingOptions();

        var results = options.Validate(new ValidationContext(options));

        Assert.Empty(results);
    }

    [Fact]
    public void Validate_AcceptsNamedDescribedRules()
    {
        var options = CreateOptions(
            new TaggingRuleOptions
            {
                Name = "Tier 0 workstations",
                Description = "Tags privileged administrative workstations.",
                BaseDn = "OU=Tier0,OU=Admin",
                ExtensionAttributes = new Dictionary<string, string>
                {
                    ["extensionAttribute1"] = "PAW"
                }
            });

        var results = options.Validate(new ValidationContext(options));

        Assert.Empty(results);
    }

    [Fact]
    public void Validate_AcceptsMultipleExtensionAttributesPerRule()
    {
        var options = CreateOptions(new TaggingRuleOptions
        {
            Name = "Servers",
            Description = "Tags servers with role and tier.",
            BaseDn = "OU=Servers",
            ExtensionAttributes = new Dictionary<string, string>
            {
                ["extensionAttribute1"] = "Server",
                ["extensionAttribute2"] = "Tier1"
            }
        });

        var results = options.Validate(new ValidationContext(options));

        Assert.Empty(results);
    }

    [Fact]
    public void Validate_AcceptsMembershipOnlyRule()
    {
        var options = CreateOptions(new TaggingRuleOptions
        {
            Name = "Protected servers",
            Description = "Assigns matching devices to managed Entra containers.",
            BaseDn = "OU=Servers",
            EntraGroupIds = [Guid.Parse("11111111-1111-1111-1111-111111111111")],
            AdministrativeUnitIds = [Guid.Parse("22222222-2222-2222-2222-222222222222")]
        });

        Assert.Empty(options.Validate(new ValidationContext(options)));
    }

    [Fact]
    public void Validate_RejectsRuleWithoutAnyActionAndDuplicateMembershipTargets()
    {
        var duplicateId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var options = CreateOptions(
            new TaggingRuleOptions
            {
                Name = "No action",
                Description = "Invalid empty action rule.",
                BaseDn = "OU=Empty"
            },
            new TaggingRuleOptions
            {
                Name = "Duplicates",
                Description = "Invalid duplicate target rule.",
                BaseDn = "OU=Servers",
                EntraGroupIds = [duplicateId, duplicateId]
            });

        var messages = options.Validate(new ValidationContext(options))
            .Select(result => result.ErrorMessage)
            .ToArray();

        Assert.Contains(messages, message => message?.Contains("requires at least one", StringComparison.Ordinal) == true);
        Assert.Contains(messages, message => message?.Contains("duplicate Entra group", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Validate_RejectsMissingMetadataAndDuplicateNames()
    {
        var options = CreateOptions(
            new TaggingRuleOptions
            {
                Name = "Servers",
                Description = "Tags servers.",
                BaseDn = "OU=Servers",
                ExtensionAttributes = new Dictionary<string, string>
                {
                    ["extensionAttribute1"] = "Server"
                }
            },
            new TaggingRuleOptions
            {
                Name = " servers ",
                BaseDn = "OU=Legacy Servers",
                ExtensionAttributes = new Dictionary<string, string>
                {
                    ["extensionAttribute2"] = "Tier1"
                }
            });

        var messages = options.Validate(new ValidationContext(options))
            .Select(result => result.ErrorMessage)
            .ToArray();

        Assert.Contains(messages, message => message?.Contains("duplicated", StringComparison.Ordinal) == true);
        Assert.Contains(messages, message => message?.Contains("Description", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData("Rule/2")]
    [InlineData("Rule\0Binary")]
    [InlineData("Rule🙂")]
    public void Validate_RejectsUnsupportedRuleNameCharacters(string ruleName)
    {
        var options = CreateOptions(new TaggingRuleOptions
        {
            Name = ruleName,
            Description = "Invalid rule name test.",
            BaseDn = "OU=Computers",
            ExtensionAttributes = new Dictionary<string, string>
            {
                ["extensionAttribute1"] = "Test"
            }
        });

        var messages = options.Validate(new ValidationContext(options))
            .Select(result => result.ErrorMessage);

        Assert.Contains(messages, message => message?.Contains("unsupported characters", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Validate_RejectsMalformedBaseDn()
    {
        var options = CreateOptions(new TaggingRuleOptions
        {
            Name = "Invalid DN",
            Description = "Invalid base DN test.",
            BaseDn = "umptata",
            ExtensionAttributes = new Dictionary<string, string>
            {
                ["extensionAttribute1"] = "Test"
            }
        });

        var messages = options.Validate(new ValidationContext(options))
            .Select(result => result.ErrorMessage);

        Assert.Contains(messages, message => message?.Contains("distinguished name", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData("extensionAttribute0")]
    [InlineData("extensionAttribute16")]
    [InlineData("displayName")]
    public void Validate_RejectsUnsupportedExtensionAttributes(string attributeName)
    {
        var options = CreateOptions(new TaggingRuleOptions
        {
            Name = "Invalid attribute",
            Description = "Tests the extension attribute allowlist.",
            BaseDn = "OU=Computers",
            ExtensionAttributes = new Dictionary<string, string>
            {
                [attributeName] = "Test"
            }
        });

        var messages = options.Validate(new ValidationContext(options))
            .Select(result => result.ErrorMessage);

        Assert.Contains(messages, message =>
            message?.Contains(
                "Only extensionAttribute1 through extensionAttribute15 are supported.",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Validate_RejectsOversizedRuleFields()
    {
        var options = CreateOptions(new TaggingRuleOptions
        {
            Name = "Oversized rule",
            Description = new string('d', TaggingOptions.MaximumRuleDescriptionLength + 1),
            BaseDn = $"OU={new string('b', TaggingOptions.MaximumBaseDnLength)}",
            ExtensionAttributes = new Dictionary<string, string>
            {
                ["extensionAttribute1"] = new string(
                    'v',
                    TaggingOptions.MaximumExtensionAttributeValueLength + 1)
            }
        });

        var messages = options.Validate(new ValidationContext(options))
            .Select(result => result.ErrorMessage)
            .ToArray();

        Assert.Contains(messages, message => message?.Contains("Description cannot exceed", StringComparison.Ordinal) == true);
        Assert.Contains(messages, message => message?.Contains("BaseDn cannot exceed", StringComparison.Ordinal) == true);
        Assert.Contains(messages, message => message?.Contains("value for", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Validate_RejectsOversizedRulesJson()
    {
        var options = new TaggingOptions
        {
            RulesJson = new string('x', TaggingOptions.MaximumRulesJsonLength + 1)
        };

        var messages = options.Validate(new ValidationContext(options))
            .Select(result => result.ErrorMessage);

        Assert.Contains(messages, message => message?.Contains("RulesJson cannot exceed", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Validate_RejectsTooManyRulesWithoutValidatingEveryRule()
    {
        var options = CreateOptions(
            Enumerable.Range(0, TaggingOptions.MaximumRuleCount + 1)
                .Select(_ => new TaggingRuleOptions())
                .ToArray());

        var results = options.Validate(new ValidationContext(options)).ToArray();

        var result = Assert.Single(results);
        Assert.Contains("more than", result.ErrorMessage, StringComparison.Ordinal);
    }

    private static TaggingOptions CreateOptions(params TaggingRuleOptions[] rules) =>
        new() { Rules = [.. rules] };
}