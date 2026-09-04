/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

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