/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

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