using HybridDeviceTagger.Models;

namespace HybridDeviceTagger.Services;

/// <summary>Provides active and staged Active Directory credentials without exposing storage details.</summary>
public interface IActiveDirectoryCredentialStore
{
    /// <summary>Gets the credential currently designated as active.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The active directory credential.</returns>
    Task<StoredDirectoryCredential> GetCurrentAsync(CancellationToken cancellationToken);

    /// <summary>Gets the staged credential used for interrupted-rotation recovery.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The staged credential, or <see langword="null"/> when none exists.</returns>
    Task<StoredDirectoryCredential?> GetNextAsync(CancellationToken cancellationToken);

    /// <summary>Stores a password in the staged credential slot.</summary>
    /// <param name="password">The password to stage.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored staged credential including its creation metadata.</returns>
    Task<StoredDirectoryCredential> StageNextAsync(
        string password,
        CancellationToken cancellationToken);

    /// <summary>Promotes a validated password to the active credential slot.</summary>
    /// <param name="password">The validated password.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task PromoteAsync(string password, CancellationToken cancellationToken);
}