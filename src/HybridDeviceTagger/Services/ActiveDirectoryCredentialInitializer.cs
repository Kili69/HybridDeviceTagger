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

using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Services;

/// <summary>Validates and initializes the Active Directory service-account credential.</summary>
public interface IActiveDirectoryCredentialInitializer
{
    /// <summary>Validates the initial password against Active Directory and stores it once.</summary>
    /// <param name="password">The current password of the configured service account.</param>
    /// <param name="cancellationToken">Cancels the bind and storage operations.</param>
    /// <returns>A task that completes when the credential has been stored.</returns>
    /// <exception cref="ActiveDirectoryCredentialValidationException">The password cannot bind to Active Directory.</exception>
    /// <exception cref="ActiveDirectoryCredentialAlreadyInitializedException">A credential has already been stored.</exception>
    Task InitializeAsync(string password, CancellationToken cancellationToken);
}

/// <summary>Initializes the active credential through the Function App managed identity.</summary>
public sealed class ActiveDirectoryCredentialInitializer : IActiveDirectoryCredentialInitializer
{
    /// <summary>Stores the active service-account credential.</summary>
    private readonly IActiveDirectoryCredentialStore _credentialStore;

    /// <summary>Tests whether a credential can bind to Active Directory.</summary>
    private readonly IActiveDirectoryPasswordClient _passwordClient;

    /// <summary>The initial Active Directory settings that identify the service account.</summary>
    private readonly ActiveDirectoryOptions _activeDirectoryOptions;

    /// <summary>Supplies the effective service-account override.</summary>
    private readonly IActiveDirectoryConfigurationStore _configurationStore;

    /// <summary>Initializes a new credential initializer.</summary>
    /// <param name="credentialStore">Stores the active service-account credential.</param>
    /// <param name="passwordClient">Tests whether a credential can bind to Active Directory.</param>
    /// <param name="activeDirectoryOptions">The Active Directory settings that identify the service account.</param>
    /// <param name="configurationStore">The service-account override store.</param>
    public ActiveDirectoryCredentialInitializer(
        IActiveDirectoryCredentialStore credentialStore,
        IActiveDirectoryPasswordClient passwordClient,
        IOptions<ActiveDirectoryOptions> activeDirectoryOptions,
        IActiveDirectoryConfigurationStore configurationStore)
    {
        _credentialStore = credentialStore;
        _passwordClient = passwordClient;
        _activeDirectoryOptions = activeDirectoryOptions.Value;
        _configurationStore = configurationStore;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(string password, CancellationToken cancellationToken)
    {
        var configuration = await _configurationStore.GetAsync(cancellationToken);
        var credential = new StoredDirectoryCredential(
            configuration.GetServiceAccount(_activeDirectoryOptions).Username,
            password,
            null);
        var validation = await _passwordClient.ValidateAccessAsync(credential, cancellationToken);
        if (!validation.Succeeded)
        {
            throw new ActiveDirectoryCredentialValidationException(validation);
        }

        await _credentialStore.InitializeCurrentAsync(password, cancellationToken);
    }
}

/// <summary>Indicates that a credential could not be validated against Active Directory.</summary>
/// <remarks>
/// Distinguishes rejected credentials from TLS, DNS, or connectivity failures so the caller sees
/// the actual cause. The message never contains the password.
/// </remarks>
public sealed class ActiveDirectoryCredentialValidationException : InvalidOperationException
{
    /// <summary>Initializes the exception from a validation result.</summary>
    /// <param name="validation">The failed validation result.</param>
    public ActiveDirectoryCredentialValidationException(ActiveDirectoryAccessValidationResult validation)
        : base(validation.InvalidCredentials
            ? "The supplied password cannot authenticate the configured Active Directory account."
            : $"Active Directory could not be reached securely, so the password was not validated: {validation.Error}")
    {
        Validation = validation;
    }

    /// <summary>Gets the validation result with every connection attempt.</summary>
    public ActiveDirectoryAccessValidationResult Validation { get; }
}