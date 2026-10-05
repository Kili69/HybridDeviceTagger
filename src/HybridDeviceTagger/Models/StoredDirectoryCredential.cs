/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

namespace HybridDeviceTagger.Models;

/// <summary>Represents an Active Directory credential loaded from secure storage.</summary>
/// <param name="Username">The LDAP bind user name.</param>
/// <param name="Password">The password, which must never be logged.</param>
/// <param name="CreatedOn">The optional Key Vault secret creation time.</param>
public sealed record StoredDirectoryCredential(
    string Username,
    string Password,
    DateTimeOffset? CreatedOn);