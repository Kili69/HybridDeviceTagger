namespace HybridDeviceTagger.Models;

public enum PasswordRotationOutcome
{
    NotDue,
    Rotated,
    RecoveredFromNext
}

public sealed record PasswordRotationResult(
    PasswordRotationOutcome Outcome,
    int? CurrentPasswordAgeDays);