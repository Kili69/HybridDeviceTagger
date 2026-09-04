/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
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
        CancellationToken cancellationToken)
    {
        var current = await _credentialStore.GetCurrentAsync(cancellationToken);
        if (!await _passwordClient.CanBindAsync(current, cancellationToken))
        {
            return await RecoverFromNextAsync(cancellationToken);
        }

        var ageDays = GetAgeDays(current.CreatedOn);
        if (ageDays is not null && ageDays < _options.RotateAfterDays)
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