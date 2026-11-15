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

using System.ComponentModel.DataAnnotations;
using Azure;
using HybridDeviceTagger.Models;
using Microsoft.Extensions.Logging;

namespace HybridDeviceTagger.Services;

/// <summary>Validates LDAP access and changes the Active Directory service account.</summary>
public interface IActiveDirectoryAccountService
{
    /// <summary>Validates LDAP access with the stored credential.</summary>
    /// <param name="cancellationToken">Cancels the validation.</param>
    /// <returns>The validation result with every connection attempt.</returns>
    /// <exception cref="ActiveDirectoryCredentialNotInitializedException">No credential is stored yet.</exception>
    Task<ActiveDirectoryAccessValidationResult> ValidateCurrentAsync(CancellationToken cancellationToken);

    /// <summary>Switches to another service account after validating its password against Active Directory.</summary>
    /// <param name="username">The new service-account UPN; may equal the current one to replace its password.</param>
    /// <param name="password">The current password of that account.</param>
    /// <param name="distinguishedName">The optional DN; empty resolves it from the UPN.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns>The stored directory configuration.</returns>
    /// <exception cref="ValidationException">The input is malformed.</exception>
    /// <exception cref="ActiveDirectoryCredentialValidationException">The credential cannot bind.</exception>
    /// <exception cref="OperationAlreadyRunningException">A password rotation is running.</exception>
    Task<ActiveDirectoryConfigurationSnapshot> ChangeAccountAsync(
        string username, string password, string? distinguishedName, CancellationToken cancellationToken);
}

/// <summary>Coordinates service-account changes with the credential and configuration stores.</summary>
/// <remarks>
/// The password-rotation lock is held while the account changes, so a concurrent rotation cannot
/// change or stage a password for the wrong account. A supplied DN must bind with the same
/// password. The new password is stored first; if the account override cannot be stored, the
/// previous password is restored.
/// </remarks>
public sealed class ActiveDirectoryAccountService : IActiveDirectoryAccountService
{
    /// <summary>Stores the active and staged passwords.</summary>
    private readonly IActiveDirectoryCredentialStore _credentialStore;

    /// <summary>Validates credentials against Active Directory.</summary>
    private readonly IActiveDirectoryPasswordClient _passwordClient;

    /// <summary>Stores the service-account override.</summary>
    private readonly IActiveDirectoryConfigurationStore _configurationStore;

    /// <summary>Serializes the change with password rotation.</summary>
    private readonly IOperationLock _operationLock;

    /// <summary>Writes failures of best-effort cleanup steps.</summary>
    private readonly ILogger<ActiveDirectoryAccountService> _logger;

    /// <summary>Initializes the service.</summary>
    /// <param name="credentialStore">Stores the active and staged passwords.</param>
    /// <param name="passwordClient">Validates credentials against Active Directory.</param>
    /// <param name="configurationStore">Stores the service-account override.</param>
    /// <param name="operationLock">Serializes the change with password rotation.</param>
    /// <param name="logger">Writes failures of best-effort cleanup steps.</param>
    public ActiveDirectoryAccountService(
        IActiveDirectoryCredentialStore credentialStore,
        IActiveDirectoryPasswordClient passwordClient,
        IActiveDirectoryConfigurationStore configurationStore,
        IOperationLock operationLock,
        ILogger<ActiveDirectoryAccountService> logger)
    {
        _credentialStore = credentialStore;
        _passwordClient = passwordClient;
        _configurationStore = configurationStore;
        _operationLock = operationLock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ActiveDirectoryAccessValidationResult> ValidateCurrentAsync(CancellationToken cancellationToken)
    {
        StoredDirectoryCredential credential;
        try
        {
            credential = await _credentialStore.GetCurrentAsync(cancellationToken);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or RequestFailedException { Status: 404 })
        {
            throw new ActiveDirectoryCredentialNotInitializedException();
        }

        return await _passwordClient.ValidateAccessAsync(credential, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ActiveDirectoryConfigurationSnapshot> ChangeAccountAsync(
        string username, string password, string? distinguishedName, CancellationToken cancellationToken)
    {
        var normalizedUsername = BlobActiveDirectoryConfigurationStore.NormalizeUsername(username);
        var normalizedDistinguishedName = BlobActiveDirectoryConfigurationStore.NormalizeDistinguishedName(distinguishedName);
        if (string.IsNullOrEmpty(password) || password.Length > 256)
        {
            throw new ValidationException("Password must contain between 1 and 256 characters.");
        }

        await using var lease = await _operationLock.TryAcquireAsync(OperationNames.PasswordRotation, cancellationToken) ??
            throw new OperationAlreadyRunningException(OperationNames.PasswordRotation);

        var validation = await _passwordClient.ValidateAccessAsync(
            new StoredDirectoryCredential(normalizedUsername, password, null),
            cancellationToken);
        if (!validation.Succeeded)
        {
            throw new ActiveDirectoryCredentialValidationException(validation);
        }

        // A DN of another account would make every later password change target the wrong
        // object. Active Directory accepts DN binds, so the DN must authenticate with the same password.
        if (normalizedDistinguishedName.Length > 0)
        {
            var distinguishedNameValidation = await _passwordClient.ValidateAccessAsync(
                new StoredDirectoryCredential(normalizedDistinguishedName, password, null),
                cancellationToken);
            if (!distinguishedNameValidation.Succeeded)
            {
                throw new ValidationException(
                    "The distinguished name does not authenticate with this password, so it does not belong to the account. " +
                    "Correct it or leave it empty to resolve it from the user principal name.");
            }
        }

        StoredDirectoryCredential? previous = null;
        try
        {
            previous = await _credentialStore.GetCurrentAsync(cancellationToken);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or RequestFailedException { Status: 404 })
        {
        }

        var configuration = await _configurationStore.GetAsync(cancellationToken);
        await _credentialStore.ReplaceCurrentAsync(password, cancellationToken);
        ActiveDirectoryConfigurationSnapshot stored;
        try
        {
            stored = await _configurationStore.ReplaceServiceAccountAsync(
                normalizedUsername, normalizedDistinguishedName, configuration.ETag, cancellationToken);
        }
        catch
        {
            if (previous is not null)
            {
                await _credentialStore.ReplaceCurrentAsync(previous.Password, CancellationToken.None);
            }
            throw;
        }

        try
        {
            await _credentialStore.ClearNextAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The staged password of the previous service account could not be invalidated");
        }

        return stored;
    }
}

/// <summary>Indicates that no Active Directory credential has been stored yet.</summary>
public sealed class ActiveDirectoryCredentialNotInitializedException()
    : InvalidOperationException("The Active Directory credential has not been initialized. Set the initial password first.");
