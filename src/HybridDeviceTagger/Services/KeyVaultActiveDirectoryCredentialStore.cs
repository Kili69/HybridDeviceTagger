/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * Copyright 2026 Andreas Lucas
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 * SPDX-License-Identifier: Apache-2.0
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
    private readonly SecretClient _secretClient;
    private readonly ActiveDirectoryOptions _activeDirectoryOptions;
    private readonly KeyVaultOptions _keyVaultOptions;
    private readonly PasswordRotationOptions _rotationOptions;
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
    public async Task<StoredDirectoryCredential> InitializeCurrentAsync(
        string password,
        CancellationToken cancellationToken)
    {
        try
        {
            await _secretClient.GetSecretAsync(
                _keyVaultOptions.CurrentPasswordSecretName,
                cancellationToken: cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return await SetSecretAsync(
                _keyVaultOptions.CurrentPasswordSecretName,
                password,
                cancellationToken);
        }

        throw new ActiveDirectoryCredentialAlreadyInitializedException();
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
    private StoredDirectoryCredential ToCredential(KeyVaultSecret secret) =>
        new(
            _activeDirectoryOptions.BindUsername,
            secret.Value,
            secret.Properties.CreatedOn);
}