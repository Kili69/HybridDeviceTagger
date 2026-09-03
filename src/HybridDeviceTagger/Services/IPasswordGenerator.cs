namespace HybridDeviceTagger.Services;

/// <summary>Generates passwords suitable for the Active Directory service account.</summary>
public interface IPasswordGenerator
{
    /// <summary>Generates a cryptographically random password.</summary>
    /// <param name="length">The required password length.</param>
    /// <returns>A password containing upper-case, lower-case, numeric, and symbol characters.</returns>
    string Generate(int length);
}