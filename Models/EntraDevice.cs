namespace HybridDeviceTagger.Models;

public sealed record EntraDevice(
    string Id,
    Guid DeviceId,
    string? DisplayName,
    string? TrustType,
    string? ExtensionAttribute1);