/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Services;

/// <summary>Validates and initializes the Active Directory service-account credential.</summary>
public interface IActiveDirectoryCredentialInitializer
{
    /// <summary>Validates the initial password against Active Directory and stores it once.</summary>
    Task InitializeAsync(string password, CancellationToken cancellationToken);
}

/// <summary>Initializes the active credential through the Function App managed identity.</summary>
public sealed class ActiveDirectoryCredentialInitializer : IActiveDirectoryCredentialInitializer
{
    private readonly IActiveDirectoryCredentialStore _credentialStore;
    private readonly IActiveDirectoryPasswordClient _passwordClient;
    private readonly ActiveDirectoryOptions _activeDirectoryOptions;

    /// <summary>Initializes a new credential initializer.</summary>
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