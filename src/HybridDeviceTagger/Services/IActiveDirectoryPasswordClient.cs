/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

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
    /// <returns>A task representing the asynchronous password change.</returns>
    Task ChangePasswordAsync(
        StoredDirectoryCredential currentCredential,
        string newPassword,
        CancellationToken cancellationToken);
}