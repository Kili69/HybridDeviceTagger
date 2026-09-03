namespace HybridDeviceTagger.Models;

/// <summary>Represents the Microsoft Entra properties required by the tagging workflow.</summary>
/// <param name="Id">The Entra object identifier.</param>
/// <param name="DeviceId">The device identifier shared with Active Directory.</param>
/// <param name="DisplayName">The optional display name.</param>
/// <param name="TrustType">The optional device trust type.</param>
/// <param name="ExtensionAttributes">The current extension attributes keyed by Graph property name.</param>
public sealed record EntraDevice(
    string Id,
    Guid DeviceId,
    string? DisplayName,
    string? TrustType,
    IReadOnlyDictionary<string, string?> ExtensionAttributes);