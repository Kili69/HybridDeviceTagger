/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using System.ComponentModel.DataAnnotations;

namespace HybridDeviceTagger.Options;

/// <summary>Configures secure LDAP access to Active Directory.</summary>
public sealed class ActiveDirectoryOptions : IValidatableObject
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "ActiveDirectory";

    /// <summary>Gets the DNS name of the domain controller.</summary>
    [Required]
    [StringLength(253, MinimumLength = 1)]
    public string Server { get; init; } = string.Empty;

    /// <summary>Gets the LDAPS port.</summary>
    [Range(1, 65535)]
    public int Port { get; init; } = 636;

    /// <summary>Gets a value indicating whether TLS is enabled for LDAP.</summary>
    public bool UseSsl { get; init; } = true;

    /// <summary>Gets a value indicating whether read operations may fall back to signed and sealed LDAP Negotiate authentication.</summary>
    public bool AllowLdapFallback { get; init; }

    /// <summary>Gets the port used for signed and sealed LDAP fallback connections.</summary>
    [Range(1, 65535)]
    public int LdapFallbackPort { get; init; } = 389;

    /// <summary>Gets the user name used for LDAP binds.</summary>
    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string BindUsername { get; init; } = string.Empty;

    /// <summary>Gets the distinguished name of the account whose password is rotated.</summary>
    [Required]
    [StringLength(2048, MinimumLength = 1)]
    public string BindUserDistinguishedName { get; init; } = string.Empty;

    /// <summary>Gets the LDAP server-side page size.</summary>
    [Range(1, 10000)]
    public int PageSize { get; init; } = 500;

    /// <summary>Gets an optional PEM or DER certificate used as a private LDAP trust anchor.</summary>
    [StringLength(4096)]
    public string? TrustedCertificatePath { get; init; }

    /// <inheritdoc />
    /// <remarks>
    /// LDAPS remains mandatory as the primary transport because password operations require it.
    /// A configured fallback must use a separate endpoint port.
    /// </remarks>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!UseSsl)
        {
            yield return new ValidationResult(
                "UseSsl must be enabled for LDAP credential and password operations.",
                [nameof(UseSsl)]);
        }

        if (AllowLdapFallback && LdapFallbackPort == Port)
        {
            yield return new ValidationResult(
                "LdapFallbackPort must differ from the LDAPS port.",
                [nameof(LdapFallbackPort), nameof(Port)]);
        }
    }
}