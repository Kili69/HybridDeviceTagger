namespace HybridDeviceTagger.Models;

/// <summary>Summarizes membership changes for one Entra device.</summary>
/// <param name="Added">Number of group or administrative-unit memberships added.</param>
/// <param name="Removed">Number of owned memberships removed.</param>
public sealed record MembershipReconciliationResult(int Added, int Removed)
{
    /// <summary>Gets a value indicating whether Graph membership changed.</summary>
    public bool Changed => Added > 0 || Removed > 0;
}