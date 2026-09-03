using Azure;
using Azure.Security.KeyVault.Secrets;
using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Services;

public sealed class KeyVaultActiveDirectoryCredentialStore : IActiveDirectoryCredentialStore
{
    private readonly SecretClient _secretClient;
    private readonly ActiveDirectoryOptions _activeDirectoryOptions;
    private readonly KeyVaultOptions _keyVaultOptions;
    private readonly PasswordRotationOptions _rotationOptions;
    private readonly TimeProvider _timeProvider;

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

    public async Task<StoredDirectoryCredential> GetCurrentAsync(
        CancellationToken cancellationToken) =>
        await GetRequiredSecretAsync(
            _keyVaultOptions.CurrentPasswordSecretName,
            cancellationToken);

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

    public async Task<StoredDirectoryCredential> StageNextAsync(
        string password,
        CancellationToken cancellationToken) =>
        await SetSecretAsync(
            _keyVaultOptions.NextPasswordSecretName,
            password,
            cancellationToken);

    public async Task PromoteAsync(string password, CancellationToken cancellationToken)
    {
        await SetSecretAsync(
            _keyVaultOptions.CurrentPasswordSecretName,
            password,
            cancellationToken);
    }

    private async Task<StoredDirectoryCredential> GetRequiredSecretAsync(
        string secretName,
        CancellationToken cancellationToken)
    {
        var response = await _secretClient.GetSecretAsync(
            secretName,
            cancellationToken: cancellationToken);
        return ToCredential(response.Value);
    }

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

    private StoredDirectoryCredential ToCredential(KeyVaultSecret secret) =>
        new(
            _activeDirectoryOptions.BindUsername,
            secret.Value,
            secret.Properties.CreatedOn);
}