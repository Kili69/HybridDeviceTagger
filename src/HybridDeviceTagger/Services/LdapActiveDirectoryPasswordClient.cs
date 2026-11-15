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

using System.Text;
using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Services;

/// <summary>Validates and changes the service-account password through TLS-protected LDAP.</summary>
public sealed class LdapActiveDirectoryPasswordClient : IActiveDirectoryPasswordClient
{
    /// <summary>The authenticated LDAP connection factory.</summary>
    private readonly ActiveDirectoryLdapConnectionFactory _connectionFactory;

    /// <summary>The service-account directory settings.</summary>
    private readonly ActiveDirectoryOptions _options;

    /// <summary>Resolves the account DN when installation had no local directory access.</summary>
    private readonly LdapActiveDirectoryDeviceSource _directorySource;

    /// <summary>Supplies the effective service-account override.</summary>
    private readonly IActiveDirectoryConfigurationStore _configurationStore;

    /// <summary>Initializes a new instance of the <see cref="LdapActiveDirectoryPasswordClient"/> class.</summary>
    /// <param name="connectionFactory">The authenticated LDAP connection factory.</param>
    /// <param name="options">The service-account directory settings.</param>
    /// <param name="directorySource">The forest-aware service-account resolver.</param>
    /// <param name="configurationStore">The service-account override store.</param>
    public LdapActiveDirectoryPasswordClient(
        ActiveDirectoryLdapConnectionFactory connectionFactory,
        IOptions<ActiveDirectoryOptions> options,
        LdapActiveDirectoryDeviceSource directorySource,
        IActiveDirectoryConfigurationStore configurationStore)
    {
        _connectionFactory = connectionFactory;
        _options = options.Value;
        _directorySource = directorySource;
        _configurationStore = configurationStore;
    }

    /// <inheritdoc />
    public async Task<ActiveDirectoryAccessValidationResult> ValidateAccessAsync(
        StoredDirectoryCredential credential,
        CancellationToken cancellationToken)
    {
        var attempts = new List<ActiveDirectoryLdapConnectionAttempt>();
        try
        {
            using var connection = await _connectionFactory.CreateBoundConnectionAsync(
                credential,
                server: null,
                cancellationToken,
                attempts);
            return new ActiveDirectoryAccessValidationResult(true, false, null, attempts);
        }
        catch (ActiveDirectoryLdapException exception)
        {
            return new ActiveDirectoryAccessValidationResult(
                false,
                exception.ResultCode == 49,
                ActiveDirectoryLdapConnectionFactory.Describe(exception),
                attempts);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // DNS discovery and trust-anchor loading fail before any LDAP attempt.
            return new ActiveDirectoryAccessValidationResult(false, false, exception.Message, attempts);
        }
    }

    /// <inheritdoc />
    public Task<bool> CanBindAsync(
        StoredDirectoryCredential credential,
        CancellationToken cancellationToken)
    {
        return CanBindAsync(
            credential,
            requireSecureConnection: false,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CanBindSecurelyAsync(
        StoredDirectoryCredential credential,
        CancellationToken cancellationToken)
    {
        return CanBindAsync(
            credential,
            requireSecureConnection: true,
            cancellationToken);
    }

    /// <summary>Tests a credential through the normal or mandatory TLS-protected bind path.</summary>
    /// <remarks>
    /// LDAP failures become <see langword="false"/> because failed credentials are an expected
    /// input to rotation recovery. Cancellation and non-LDAP failures still propagate.
    /// </remarks>
    /// <param name="credential">The credential to test.</param>
    /// <param name="requireSecureConnection">Whether only the TLS-protected bind path may be used.</param>
    /// <param name="cancellationToken">Cancels the bind.</param>
    /// <returns><see langword="true"/> when the credential binds successfully.</returns>
    private async Task<bool> CanBindAsync(
        StoredDirectoryCredential credential,
        bool requireSecureConnection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var connection = requireSecureConnection
                ? await _connectionFactory.CreateSecureBoundConnectionAsync(
                    credential,
                    cancellationToken)
                : await _connectionFactory.CreateBoundConnectionAsync(
                    credential,
                    server: null,
                    cancellationToken);
            return true;
        }
        catch (ActiveDirectoryLdapException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Active Directory receives one atomic modify request that deletes the quoted UTF-16 old
    /// password and adds the quoted UTF-16 new password.
    /// </remarks>
    public async Task ChangePasswordAsync(
        StoredDirectoryCredential currentCredential,
        string newPassword,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var distinguishedName = await GetDistinguishedNameAsync(cancellationToken);
        using var connection = await _connectionFactory.CreateSecureBoundConnectionAsync(
            currentCredential,
            cancellationToken);
        await connection.ChangePasswordAsync(
            distinguishedName,
            ToUnicodePassword(currentCredential.Password),
            ToUnicodePassword(newPassword),
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Reads <c>pwdLastSet</c> of the account and the minimum password age of its resultant
    /// fine-grained password policy. When no policy applies or it cannot be read, the domain's
    /// <c>minPwdAge</c> is used. Active Directory stores both ages as negative 100-nanosecond intervals.
    /// </remarks>
    public async Task<PasswordChangeWindow?> GetPasswordChangeWindowAsync(
        StoredDirectoryCredential credential,
        CancellationToken cancellationToken)
    {
        var distinguishedName = await GetDistinguishedNameAsync(cancellationToken);
        using var connection = await _connectionFactory.CreateSecureBoundConnectionAsync(credential, cancellationToken);
        var account = (await connection.SearchAsync(
            distinguishedName,
            "(objectClass=*)",
            ActiveDirectoryLdapSearchScope.Base,
            ["pwdLastSet", "msDS-ResultantPSO"],
            pageSize: null,
            cancellationToken)).FirstOrDefault();
        if (!long.TryParse(account?.GetString("pwdLastSet"), out var lastSetFileTime) || lastSetFileTime <= 0)
        {
            return null;
        }

        var lastSet = DateTimeOffset.FromFileTime(lastSetFileTime).ToUniversalTime();
        if (account!.GetString("msDS-ResultantPSO") is { Length: > 0 } policyDn)
        {
            try
            {
                var policy = (await connection.SearchAsync(
                    policyDn,
                    "(objectClass=*)",
                    ActiveDirectoryLdapSearchScope.Base,
                    ["msDS-MinimumPasswordAge"],
                    pageSize: null,
                    cancellationToken)).FirstOrDefault();
                if (ParseInterval(policy?.GetString("msDS-MinimumPasswordAge")) is { } policyAge)
                {
                    return new PasswordChangeWindow(lastSet, policyAge, "fineGrainedPolicy");
                }
            }
            catch (ActiveDirectoryLdapException)
            {
                // Reading password settings objects usually requires delegated rights; fall back to the domain.
            }
        }

        var domainIndex = distinguishedName.IndexOf(",DC=", StringComparison.OrdinalIgnoreCase);
        var domainDn = domainIndex < 0 ? distinguishedName : distinguishedName[(domainIndex + 1)..];
        var domain = (await connection.SearchAsync(
            domainDn,
            "(objectClass=*)",
            ActiveDirectoryLdapSearchScope.Base,
            ["minPwdAge"],
            pageSize: null,
            cancellationToken)).FirstOrDefault();
        return ParseInterval(domain?.GetString("minPwdAge")) is { } domainAge
            ? new PasswordChangeWindow(lastSet, domainAge, "domain")
            : null;
    }

    /// <summary>Converts an Active Directory interval (negative 100-nanosecond units) to a time span.</summary>
    /// <param name="value">The attribute value.</param>
    /// <returns>The interval, or null when the value is missing or malformed.</returns>
    internal static TimeSpan? ParseInterval(string? value) =>
        long.TryParse(value, out var ticks) && ticks != long.MinValue ? TimeSpan.FromTicks(Math.Abs(ticks)) : null;

    /// <summary>Gets the configured service-account DN or resolves it from the UPN.</summary>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>The service-account distinguished name.</returns>
    private async Task<string> GetDistinguishedNameAsync(CancellationToken cancellationToken)
    {
        var (username, configuredDistinguishedName) =
            (await _configurationStore.GetAsync(cancellationToken)).GetServiceAccount(_options);
        return string.IsNullOrWhiteSpace(configuredDistinguishedName)
            ? await _directorySource.ResolveServiceAccountDistinguishedNameAsync(username, cancellationToken)
            : configuredDistinguishedName;
    }

    /// <summary>Encodes the quoted UTF-16LE representation required by AD's <c>unicodePwd</c>.</summary>
    /// <remarks>Omitting the surrounding quotes causes Active Directory to reject the syntax.</remarks>
    /// <param name="password">The plain-text password.</param>
    /// <returns>The encoded <c>unicodePwd</c> value.</returns>
    private static byte[] ToUnicodePassword(string password) =>
        Encoding.Unicode.GetBytes($"\"{password}\"");
}