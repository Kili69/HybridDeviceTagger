namespace HybridDeviceTagger.Models;

/// <summary>Represents an Active Directory computer that has a hybrid join device identifier.</summary>
/// <param name="DeviceId">The value of the <c>msDS-DeviceID</c> attribute.</param>
/// <param name="Name">The computer name.</param>
/// <param name="DistinguishedName">The computer object's distinguished name.</param>
public sealed record DirectoryDevice(Guid DeviceId, string Name, string DistinguishedName);