/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

namespace HybridDeviceTagger.Models;

/// <summary>Identifies the result of a password rotation check.</summary>
public enum PasswordRotationOutcome
{
    /// <summary>The active password has not reached its configured rotation age.</summary>
    NotDue,
    /// <summary>A new password was staged, applied, validated, and promoted.</summary>
    Rotated,
    /// <summary>The staged password worked and was promoted after the active credential failed.</summary>
    RecoveredFromNext,
    /// <summary>Rotation was due but deferred because no secure LDAP channel was available.</summary>
    DeferredNoSecureConnection
}

/// <summary>Describes the outcome of a password rotation check.</summary>
/// <param name="Outcome">The action performed.</param>
/// <param name="CurrentPasswordAgeDays">The known age of the active password, if available.</param>
public sealed record PasswordRotationResult(
    PasswordRotationOutcome Outcome,
    int? CurrentPasswordAgeDays);