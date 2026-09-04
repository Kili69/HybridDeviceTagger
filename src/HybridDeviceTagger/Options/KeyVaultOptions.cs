/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

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