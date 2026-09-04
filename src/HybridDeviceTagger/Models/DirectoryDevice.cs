namespace HybridDeviceTagger.Models;

/// <summary>Represents an Active Directory computer that has a hybrid join device identifier.</summary>
/// <param name="DeviceId">The AD <c>objectGUID</c> mapped to the Entra <c>deviceId</c>.</param>
/// <param name="Name">The computer name.</param>
/// <param name="DistinguishedName">The computer object's distinguished name.</param>
public sealed record DirectoryDevice(Guid DeviceId, string Name, string DistinguishedName);