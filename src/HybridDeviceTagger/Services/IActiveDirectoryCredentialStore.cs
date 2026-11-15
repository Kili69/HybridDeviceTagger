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

namespace HybridDeviceTagger.Services;

/// <summary>Provides active and staged Active Directory credentials without exposing storage details.</summary>
public interface IActiveDirectoryCredentialStore
{
    /// <summary>Stores the initial active credential only when no current secret exists.</summary>
    /// <param name="password">The validated initial password.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The initialized active credential including its creation metadata.</returns>
    Task<StoredDirectoryCredential> InitializeCurrentAsync(
        string password,
        CancellationToken cancellationToken);

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
    /// <returns>A task representing the asynchronous promotion.</returns>
    Task PromoteAsync(string password, CancellationToken cancellationToken);
}

/// <summary>Indicates that the active credential has already been initialized.</summary>
public sealed class ActiveDirectoryCredentialAlreadyInitializedException()
    : InvalidOperationException("The Active Directory credential has already been initialized.");