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

    public string? BindUsername { get; init; }

    public string? BindPassword { get; init; }

    [Range(1, 10000)]
    public int PageSize { get; init; } = 500;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var hasUsername = !string.IsNullOrWhiteSpace(BindUsername);
        var hasPassword = !string.IsNullOrWhiteSpace(BindPassword);

        if (hasUsername != hasPassword)
        {
            yield return new ValidationResult(
                "BindUsername and BindPassword must either both be set or both be omitted.",
                [nameof(BindUsername), nameof(BindPassword)]);
        }

        if (hasUsername && !UseSsl)
        {
            yield return new ValidationResult(
                "UseSsl must be enabled when LDAP bind credentials are configured.",
                [nameof(UseSsl)]);
        }
    }
}