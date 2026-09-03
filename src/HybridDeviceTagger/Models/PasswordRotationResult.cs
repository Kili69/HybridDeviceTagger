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