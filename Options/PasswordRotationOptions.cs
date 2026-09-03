using System.ComponentModel.DataAnnotations;

namespace HybridDeviceTagger.Options;

public sealed class PasswordRotationOptions : IValidatableObject
{
    public const string SectionName = "PasswordRotation";

    [Range(1, 29)]
    public int RotateAfterDays { get; init; } = 25;

    [Range(20, 128)]
    public int PasswordLength { get; init; } = 40;

    [Range(2, 90)]
    public int SecretExpiryDays { get; init; } = 35;

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