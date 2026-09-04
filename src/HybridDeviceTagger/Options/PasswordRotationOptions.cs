using System.ComponentModel.DataAnnotations;

namespace HybridDeviceTagger.Options;

/// <summary>Configures age-based Active Directory password rotation.</summary>
public sealed class PasswordRotationOptions : IValidatableObject
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "PasswordRotation";

    /// <summary>Gets the password age in days at which rotation is due.</summary>
    [Range(1, 29)]
    public int RotateAfterDays { get; init; } = 25;

    /// <summary>Gets the generated password length.</summary>
    [Range(20, 128)]
    public int PasswordLength { get; init; } = 40;

    /// <summary>Gets the lifetime in days assigned to staged Key Vault secrets.</summary>
    [Range(2, 90)]
    public int SecretExpiryDays { get; init; } = 35;

    /// <inheritdoc />
    /// <remarks>
    /// Secret versions must outlive the rotation threshold so the staged credential remains
    /// available throughout the normal recovery window.
    /// </remarks>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SecretExpiryDays <= RotateAfterDays)
        {
            yield return new ValidationResult(
                "SecretExpiryDays must be greater than RotateAfterDays.",
                [nameof(SecretExpiryDays), nameof(RotateAfterDays)]);
        }
    }
}