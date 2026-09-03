using System.ComponentModel.DataAnnotations;

namespace HybridDeviceTagger.Options;

public sealed class KeyVaultOptions
{
    public const string SectionName = "KeyVault";

    [Required]
    public string VaultUri { get; init; } = string.Empty;

    [Required]
    public string CurrentPasswordSecretName { get; init; } =
        "hybrid-device-tagger-ad-password-current";

    [Required]
    public string NextPasswordSecretName { get; init; } =
        "hybrid-device-tagger-ad-password-next";
}