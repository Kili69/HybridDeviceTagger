using System.ComponentModel.DataAnnotations;

namespace HybridDeviceTagger.Options;

public sealed class TaggingOptions
{
    public const string SectionName = "Tagging";

    [Required]
    public string ExtensionAttribute1Value { get; init; } = string.Empty;
}