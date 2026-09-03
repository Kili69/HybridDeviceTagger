using HybridDeviceTagger.Models;

namespace HybridDeviceTagger.Services;

/// <summary>Validates and changes the configured Active Directory service-account password.</summary>
public interface IActiveDirectoryPasswordClient
{
    /// <summary>Tests whether a credential can complete an LDAP bind.</summary>
    /// <param name="credential">The credential to test.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> when the bind succeeds; otherwise <see langword="false"/>.</returns>
    Task<bool> CanBindAsync(
        StoredDirectoryCredential credential,
        CancellationToken cancellationToken);

    /// <summary>Tests whether a credential can complete an LDAPS bind without fallback.</summary>
    /// <param name="credential">The credential to test.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> when the secure bind succeeds; otherwise <see langword="false"/>.</returns>
    Task<bool> CanBindSecurelyAsync(
        StoredDirectoryCredential credential,
        CancellationToken cancellationToken);

    /// <summary>Atomically changes the service-account password over LDAPS.</summary>
    /// <param name="currentCredential">The credential currently accepted by AD.</param>
    /// <param name="newPassword">The replacement password.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task ChangePasswordAsync(
        StoredDirectoryCredential currentCredential,
        string newPassword,
        CancellationToken cancellationToken);
}