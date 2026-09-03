namespace HybridDeviceTagger.Models;

public sealed record TaggingResult(
    int Discovered,
    int Updated,
    int Unchanged,
    int NotFound,
    int NotHybridJoined);