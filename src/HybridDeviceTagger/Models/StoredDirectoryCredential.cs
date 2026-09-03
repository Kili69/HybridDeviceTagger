namespace HybridDeviceTagger.Models;

/// <summary>Represents an Active Directory credential loaded from secure storage.</summary>
/// <param name="Username">The LDAP bind user name.</param>
/// <param name="Password">The password, which must never be logged.</param>
/// <param name="CreatedOn">The optional Key Vault secret creation time.</param>
public sealed record StoredDirectoryCredential(
    string Username,
    string Password,
    DateTimeOffset? CreatedOn);