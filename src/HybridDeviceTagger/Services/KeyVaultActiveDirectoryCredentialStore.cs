/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using Azure;
using Azure.Security.KeyVault.Secrets;
using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Services;

/// <summary>Stores active and staged Active Directory passwords as Azure Key Vault secrets.</summary>
public sealed class KeyVaultActiveDirectoryCredentialStore : IActiveDirectoryCredentialStore
{
    /// <summary>The Key Vault client authenticated with the Function App's managed identity.</summary>
    private readonly SecretClient _secretClient;

    /// <summary>Supplies the bind user name combined with each stored password.</summary>
    private readonly ActiveDirectoryOptions _activeDirectoryOptions;

    /// <summary>Supplies the names of the current and next password secrets.</summary>
    private readonly KeyVaultOptions _keyVaultOptions;

    /// <summary>Supplies the expiry assigned to new secret versions.</summary>
    private readonly PasswordRotationOptions _rotationOptions;

    /// <summary>Supplies the UTC time used to compute secret expiry.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="KeyVaultActiveDirectoryCredentialStore"/> class.</summary>
    /// <param name="secretClient">The authenticated Key Vault client.</param>
    /// <param name="activeDirectoryOptions">The service-account settings.</param>
    /// <param name="keyVaultOptions">The secret-name settings.</param>
    /// <param name="rotationOptions">The staged-secret lifetime settings.</param>
    /// <param name="timeProvider">The UTC time source.</param>
    public KeyVaultActiveDirectoryCredentialStore(
        SecretClient secretClient,
        IOptions<ActiveDirectoryOptions> activeDirectoryOptions,
        IOptions<KeyVaultOptions> keyVaultOptions,
        IOptions<PasswordRotationOptions> rotationOptions,
        TimeProvider timeProvider)
    {
        _secretClient = secretClient;
        _activeDirectoryOptions = activeDirectoryOptions.Value;
        _keyVaultOptions = keyVaultOptions.Value;
        _rotationOptions = rotationOptions.Value;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<StoredDirectoryCredential> GetCurrentAsync(
        CancellationToken cancellationToken) =>
        await GetRequiredSecretAsync(
            _keyVaultOptions.CurrentPasswordSecretName,
            cancellationToken);

    /// <inheritdoc />
    public async Task<StoredDirectoryCredential?> GetNextAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await GetRequiredSecretAsync(
                _keyVaultOptions.NextPasswordSecretName,
                cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<StoredDirectoryCredential> StageNextAsync(
        string password,
        CancellationToken cancellationToken) =>
        await SetSecretAsync(
            _keyVaultOptions.NextPasswordSecretName,
            password,
            cancellationToken);

    /// <inheritdoc />
    public async Task PromoteAsync(string password, CancellationToken cancellationToken)
    {
        await SetSecretAsync(
            _keyVaultOptions.CurrentPasswordSecretName,
            password,
            cancellationToken);
    }

    /// <summary>Loads a required secret and converts its value and metadata to a bind credential.</summary>
    /// <remarks>A missing required secret is allowed to propagate as a Key Vault error.</remarks>
    /// <param name="secretName">The Key Vault secret name.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The credential built from the latest secret version.</returns>
    /// <exception cref="RequestFailedException">The secret does not exist or cannot be read.</exception>
    private async Task<StoredDirectoryCredential> GetRequiredSecretAsync(
        string secretName,
        CancellationToken cancellationToken)
    {
        var response = await _secretClient.GetSecretAsync(
            secretName,
            cancellationToken: cancellationToken);
        return ToCredential(response.Value);
    }

    /// <summary>Creates a new secret version with the configured recovery lifetime.</summary>
    /// <remarks>
    /// The same operation is used to stage <c>next</c> and to promote a validated password to
    /// <c>current</c>. Existing versions remain available according to Key Vault retention policy.
    /// </remarks>
    /// <param name="secretName">The Key Vault secret name.</param>
    /// <param name="password">The password stored as the secret value.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The credential built from the new secret version.</returns>
    private async Task<StoredDirectoryCredential> SetSecretAsync(
        string secretName,
        string password,
        CancellationToken cancellationToken)
    {
        var secret = new KeyVaultSecret(secretName, password);
        secret.Properties.ExpiresOn = _timeProvider.GetUtcNow()
            .AddDays(_rotationOptions.SecretExpiryDays);
        var response = await _secretClient.SetSecretAsync(secret, cancellationToken);
        return ToCredential(response.Value);
    }

    /// <summary>Combines a secret password and creation timestamp with the configured bind user.</summary>
    /// <param name="secret">The Key Vault secret.</param>
    /// <returns>The directory credential.</returns>
    private StoredDirectoryCredential ToCredential(KeyVaultSecret secret) =>
        new(
            _activeDirectoryOptions.BindUsername,
            secret.Value,
            secret.Properties.CreatedOn);
}