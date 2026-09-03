using System.ComponentModel.DataAnnotations;

namespace HybridDeviceTagger.Options;

public sealed class ActiveDirectoryOptions : IValidatableObject
{
    public const string SectionName = "ActiveDirectory";

    [Required]
    public string Server { get; init; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; init; } = 636;

    public bool UseSsl { get; init; } = true;

    [Required]
    public string BaseDn { get; init; } = string.Empty;

    [Required]
    public string BindUsername { get; init; } = string.Empty;

    [Required]
    public string BindUserDistinguishedName { get; init; } = string.Empty;

    [Range(1, 10000)]
    public int PageSize { get; init; } = 500;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!UseSsl)
        {
            yield return new ValidationResult(
                "UseSsl must be enabled for LDAP credential and password operations.",
                [nameof(UseSsl)]);
        }
    }
}