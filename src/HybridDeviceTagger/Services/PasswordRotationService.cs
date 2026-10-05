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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Services;

/// <summary>Coordinates recoverable, age-based Active Directory password rotation.</summary>
public sealed class PasswordRotationService
{
    private readonly IActiveDirectoryCredentialStore _credentialStore;
    private readonly IActiveDirectoryPasswordClient _passwordClient;
    private readonly IPasswordGenerator _passwordGenerator;
    private readonly PasswordRotationOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PasswordRotationService> _logger;

    /// <summary>Initializes a new instance of the <see cref="PasswordRotationService"/> class.</summary>
    /// <param name="credentialStore">The active and staged credential store.</param>
    /// <param name="passwordClient">The directory password client.</param>
    /// <param name="passwordGenerator">The secure password generator.</param>
    /// <param name="options">The rotation policy.</param>
    /// <param name="timeProvider">The UTC time source.</param>
    /// <param name="logger">The structured logger.</param>
    public PasswordRotationService(
        IActiveDirectoryCredentialStore credentialStore,
        IActiveDirectoryPasswordClient passwordClient,
        IPasswordGenerator passwordGenerator,
        IOptions<PasswordRotationOptions> options,
        TimeProvider timeProvider,
        ILogger<PasswordRotationService> logger)
    {
        _credentialStore = credentialStore;
        _passwordClient = passwordClient;
        _passwordGenerator = passwordGenerator;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Recovers a staged credential or performs a validated two-secret rotation.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The rotation outcome and known credential age.</returns>
    /// <remarks>
    /// A generated password is staged before AD is changed and promoted only after a successful
    /// bind. If validation fails, the staged secret remains available for recovery.
    /// </remarks>
    public async Task<PasswordRotationResult> RotateIfDueAsync(
        CancellationToken cancellationToken) =>
        await RotateAsync(false, cancellationToken);

    /// <summary>Performs a rotation immediately while preserving recovery and secure-bind checks.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The rotation outcome and known credential age.</returns>
    public async Task<PasswordRotationResult> RotateNowAsync(
        CancellationToken cancellationToken) =>
        await RotateAsync(true, cancellationToken);

    private async Task<PasswordRotationResult> RotateAsync(
        bool force,
        CancellationToken cancellationToken)
    {
        var current = await _credentialStore.GetCurrentAsync(cancellationToken);
        if (!await _passwordClient.CanBindAsync(current, cancellationToken))
        {
            return await RecoverFromNextAsync(cancellationToken);
        }

        var ageDays = GetAgeDays(current.CreatedOn);
        if (!force && ageDays is not null && ageDays < _options.RotateAfterDays)
        {
            return new PasswordRotationResult(PasswordRotationOutcome.NotDue, ageDays);
        }

        if (!await _passwordClient.CanBindSecurelyAsync(current, cancellationToken))
        {
            _logger.LogWarning(
                "Password rotation is due but was deferred because LDAPS is unavailable.");
            return new PasswordRotationResult(
                PasswordRotationOutcome.DeferredNoSecureConnection,
                ageDays);
        }

        var newPassword = _passwordGenerator.Generate(_options.PasswordLength);
        var staged = await _credentialStore.StageNextAsync(newPassword, cancellationToken);
        await _passwordClient.ChangePasswordAsync(current, newPassword, cancellationToken);

        if (!await _passwordClient.CanBindSecurelyAsync(staged, cancellationToken))
        {
            throw new InvalidOperationException(
                "The AD password changed, but validation with the staged credential failed. " +
                "The next secret remains available for recovery.");
        }

        await _credentialStore.PromoteAsync(newPassword, cancellationToken);
        _logger.LogInformation("The Active Directory service account password was rotated.");
        return new PasswordRotationResult(PasswordRotationOutcome.Rotated, ageDays);
    }

    /// <summary>Recovers an interrupted rotation by validating and promoting the staged secret.</summary>
    /// <remarks>
    /// Recovery is attempted only after <c>current</c> can no longer bind. If neither credential
    /// works, the method fails without overwriting either secret so an operator can investigate.
    /// </remarks>
    private async Task<PasswordRotationResult> RecoverFromNextAsync(
        CancellationToken cancellationToken)
    {
        var next = await _credentialStore.GetNextAsync(cancellationToken);
        if (next is null || !await _passwordClient.CanBindAsync(next, cancellationToken))
        {
            throw new InvalidOperationException(
                "Neither the current nor the next Key Vault credential can bind to Active Directory.");
        }

        await _credentialStore.PromoteAsync(next.Password, cancellationToken);
        _logger.LogWarning(
            "Recovered the Active Directory credential by promoting the staged next secret.");
        return new PasswordRotationResult(
            PasswordRotationOutcome.RecoveredFromNext,
            GetAgeDays(next.CreatedOn));
    }

            /// <summary>Calculates whole elapsed UTC days from Key Vault creation metadata.</summary>
            /// <returns>The non-negative age, or <see langword="null"/> when metadata is unavailable.</returns>
    private int? GetAgeDays(DateTimeOffset? createdOn) =>
        createdOn is null
            ? null
            : Math.Max(0, (int)(_timeProvider.GetUtcNow() - createdOn.Value).TotalDays);
}