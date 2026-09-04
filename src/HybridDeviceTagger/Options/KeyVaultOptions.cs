using System.ComponentModel.DataAnnotations;

namespace HybridDeviceTagger.Options;

/// <summary>Configures the Key Vault used for Active Directory credentials.</summary>
public sealed class KeyVaultOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "KeyVault";

    /// <summary>Gets the Key Vault service URI.</summary>
    [Required]
    [StringLength(2048, MinimumLength = 1)]
    [Url]
    public string VaultUri { get; init; } = string.Empty;

    /// <summary>Gets the secret name containing the active password.</summary>
    [Required]
    [StringLength(127, MinimumLength = 1)]
    [RegularExpression("^[0-9A-Za-z-]+$")]
    public string CurrentPasswordSecretName { get; init; } =
        "hybrid-device-tagger-ad-password-current";

    /// <summary>Gets the secret name containing the staged recovery password.</summary>
    [Required]
    [StringLength(127, MinimumLength = 1)]
    [RegularExpression("^[0-9A-Za-z-]+$")]
    public string NextPasswordSecretName { get; init; } =
        "hybrid-device-tagger-ad-password-next";
}