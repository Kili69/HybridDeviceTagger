namespace HybridDeviceTagger.Models;

/// <summary>Summarizes one idempotent device-tagging run.</summary>
/// <param name="Discovered">Number of AD computers discovered.</param>
/// <param name="Updated">Number of Entra devices updated.</param>
/// <param name="Unchanged">Number already carrying the configured value.</param>
/// <param name="NotFound">Number without a matching Entra device.</param>
/// <param name="NotHybridJoined">Number whose Entra trust type was not <c>ServerAd</c>.</param>
public sealed record TaggingResult(
    int Discovered,
    int Updated,
    int Unchanged,
    int NotFound,
    int NotHybridJoined);