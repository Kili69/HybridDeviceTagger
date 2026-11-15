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

    /// <summary>The Active Directory settings that identify the service account.</summary>
    private readonly ActiveDirectoryOptions _activeDirectoryOptions;

    /// <summary>Initializes a new credential initializer.</summary>
    /// <param name="credentialStore">Stores the active service-account credential.</param>
    /// <param name="passwordClient">Tests whether a credential can bind to Active Directory.</param>
    /// <param name="activeDirectoryOptions">The Active Directory settings that identify the service account.</param>
    public ActiveDirectoryCredentialInitializer(
        IActiveDirectoryCredentialStore credentialStore,
        IActiveDirectoryPasswordClient passwordClient,
        IOptions<ActiveDirectoryOptions> activeDirectoryOptions)
    {
        _credentialStore = credentialStore;
        _passwordClient = passwordClient;
        _activeDirectoryOptions = activeDirectoryOptions.Value;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(string password, CancellationToken cancellationToken)
    {
        var credential = new StoredDirectoryCredential(
            _activeDirectoryOptions.BindUsername,
            password,
            null);
        if (!await _passwordClient.CanBindAsync(credential, cancellationToken))
        {
            throw new ActiveDirectoryCredentialValidationException();
        }

        await _credentialStore.InitializeCurrentAsync(password, cancellationToken);
    }
}

/// <summary>Indicates that the supplied password cannot authenticate the configured AD account.</summary>
public sealed class ActiveDirectoryCredentialValidationException()
    : InvalidOperationException("The supplied password cannot authenticate the configured Active Directory account.");