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
    /// <summary>
    /// Content type of the unusable placeholder secrets that the deployment creates in an existing
    /// Key Vault so the managed identity can be authorized on the two password secrets only.
    /// </summary>
    internal const string PlaceholderContentType = "application/vnd.hybriddevicetagger.placeholder";

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
    /// <remarks>
    /// Only a missing secret or a deployment placeholder is replaced. A real password is never
    /// overwritten, so the endpoint cannot be used to substitute the service-account credential.
    /// </remarks>
    public async Task<StoredDirectoryCredential> InitializeCurrentAsync(
        string password,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _secretClient.GetSecretAsync(
                _keyVaultOptions.CurrentPasswordSecretName,
                cancellationToken: cancellationToken);
            if (!IsPlaceholder(existing.Value))
            {
                throw new ActiveDirectoryCredentialAlreadyInitializedException();
            }
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
        }

        return await SetSecretAsync(
            _keyVaultOptions.CurrentPasswordSecretName,
            password,
            cancellationToken);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The current secret is still a deployment placeholder.</exception>
    public async Task<StoredDirectoryCredential> GetCurrentAsync(
        CancellationToken cancellationToken)
    {
        var response = await _secretClient.GetSecretAsync(
            _keyVaultOptions.CurrentPasswordSecretName,
            cancellationToken: cancellationToken);
        if (IsPlaceholder(response.Value))
        {
            throw new InvalidOperationException(
                "The Active Directory credential has not been initialized. Initialize it through the management API.");
        }

        return ToCredential(response.Value);
    }

    /// <inheritdoc />
    public async Task<StoredDirectoryCredential?> GetNextAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _secretClient.GetSecretAsync(
                _keyVaultOptions.NextPasswordSecretName,
                cancellationToken: cancellationToken);
            return IsPlaceholder(response.Value) ? null : ToCredential(response.Value);
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

    /// <summary>Returns whether a secret is an unusable deployment placeholder.</summary>
    /// <param name="secret">The Key Vault secret.</param>
    /// <returns><see langword="true"/> when the secret carries the placeholder content type.</returns>
    internal static bool IsPlaceholder(KeyVaultSecret secret) =>
        string.Equals(secret.Properties.ContentType, PlaceholderContentType, StringComparison.Ordinal);

    /// <summary>Combines a secret password and creation timestamp with the configured bind user.</summary>
    /// <param name="secret">The Key Vault secret.</param>
    /// <returns>The directory credential.</returns>
    private StoredDirectoryCredential ToCredential(KeyVaultSecret secret) =>
        new(
            _activeDirectoryOptions.BindUsername,
            secret.Value,
            secret.Properties.CreatedOn);
}