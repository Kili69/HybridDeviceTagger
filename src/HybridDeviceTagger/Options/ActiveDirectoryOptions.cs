using System.ComponentModel.DataAnnotations;

namespace HybridDeviceTagger.Options;

/// <summary>Configures secure LDAP access to Active Directory.</summary>
public sealed class ActiveDirectoryOptions : IValidatableObject
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "ActiveDirectory";

    /// <summary>Gets the DNS name of the domain controller.</summary>
    [Required]
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
    public string BindUsername { get; init; } = string.Empty;

    /// <summary>Gets the distinguished name of the account whose password is rotated.</summary>
    [Required]
    public string BindUserDistinguishedName { get; init; } = string.Empty;

    /// <summary>Gets the LDAP server-side page size.</summary>
    [Range(1, 10000)]
    public int PageSize { get; init; } = 500;

    /// <inheritdoc />
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