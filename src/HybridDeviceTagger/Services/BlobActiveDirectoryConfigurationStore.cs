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
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HybridDeviceTagger.Options;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Services;

/// <summary>Represents the effective, versioned Active Directory forest configuration.</summary>
/// <param name="Domain">The forest DNS domain used for domain-controller discovery.</param>
/// <param name="ETag">The stored version, or null until the first management change.</param>
/// <param name="LastModifiedUtc">The last stored modification time.</param>
/// <param name="Source">The source, either appSetting or blob.</param>
/// <param name="BindUsername">The service-account UPN override, or null to use the application setting.</param>
/// <param name="BindUserDistinguishedName">
/// The service-account DN override; empty resolves it from the UPN. Only used with <paramref name="BindUsername"/>.
/// </param>
public sealed record ActiveDirectoryConfigurationSnapshot(
    string Domain,
    string? ETag,
    DateTimeOffset? LastModifiedUtc,
    string Source,
    string? BindUsername = null,
    string? BindUserDistinguishedName = null)
{
    /// <summary>Returns the effective service account from the override or the application settings.</summary>
    /// <param name="options">The initial application settings.</param>
    /// <returns>The UPN and the DN, which is empty when it must be resolved from the UPN.</returns>
    public (string Username, string DistinguishedName) GetServiceAccount(ActiveDirectoryOptions options) =>
        string.IsNullOrWhiteSpace(BindUsername)
            ? (options.BindUsername, options.BindUserDistinguishedName ?? string.Empty)
            : (BindUsername, BindUserDistinguishedName ?? string.Empty);
}

/// <summary>Reads and conditionally updates the forest domain independently of AD connectivity.</summary>
public interface IActiveDirectoryConfigurationStore
{
    /// <summary>Gets the persisted forest domain or the initial application setting.</summary>
    /// <param name="cancellationToken">Cancels the storage request.</param>
    /// <returns>The current configuration and version.</returns>
    Task<ActiveDirectoryConfigurationSnapshot> GetAsync(CancellationToken cancellationToken);

    /// <summary>Validates and stores a forest domain without requiring a working LDAP connection.</summary>
    /// <remarks>A stored service-account override is preserved.</remarks>
    /// <param name="domain">The new forest DNS domain.</param>
    /// <param name="expectedETag">The expected version; null creates the first stored override.</param>
    /// <param name="cancellationToken">Cancels the storage request.</param>
    /// <returns>The stored configuration and version.</returns>
    Task<ActiveDirectoryConfigurationSnapshot> ReplaceAsync(
        string domain, string? expectedETag, CancellationToken cancellationToken);

    /// <summary>Stores a service-account override while preserving the forest domain.</summary>
    /// <param name="username">The service-account UPN.</param>
    /// <param name="distinguishedName">The optional DN; empty resolves it from the UPN.</param>
    /// <param name="expectedETag">The expected version; null creates the first stored override.</param>
    /// <param name="cancellationToken">Cancels the storage request.</param>
    /// <returns>The stored configuration and version.</returns>
    /// <exception cref="ValidationException">The UPN or DN is malformed.</exception>
    Task<ActiveDirectoryConfigurationSnapshot> ReplaceServiceAccountAsync(
        string username, string? distinguishedName, string? expectedETag, CancellationToken cancellationToken);
}

/// <summary>Persists the forest domain and service-account override in private Storage, shared by every Function instance.</summary>
/// <remarks>
/// Older versions stored only a JSON string with the domain. That format is still read; every
/// write uses the JSON object format.
/// </remarks>
public sealed class BlobActiveDirectoryConfigurationStore : IActiveDirectoryConfigurationStore
{
    /// <summary>The stable name of the forest-domain configuration blob.</summary>
    public const string BlobName = "active-directory-configuration.json";

    /// <summary>The blob containing the management override.</summary>
    private readonly BlobClient _blobClient;

    /// <summary>The initial application settings used when no override exists.</summary>
    private readonly ActiveDirectoryOptions _options;

    /// <summary>Initializes the private Blob-backed store.</summary>
    /// <param name="blobClient">The configuration blob.</param>
    /// <param name="options">The validated initial directory settings.</param>
    public BlobActiveDirectoryConfigurationStore(BlobClient blobClient, IOptions<ActiveDirectoryOptions> options)
    {
        _blobClient = blobClient;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<ActiveDirectoryConfigurationSnapshot> GetAsync(CancellationToken cancellationToken) =>
        await ReadAsync(cancellationToken) ??
        new ActiveDirectoryConfigurationSnapshot(NormalizeDomain(_options.Domain), null, null, "appSetting");

    /// <inheritdoc />
    public async Task<ActiveDirectoryConfigurationSnapshot> ReplaceAsync(
        string domain, string? expectedETag, CancellationToken cancellationToken)
    {
        var normalized = NormalizeDomain(domain);
        var current = await ReadAsync(cancellationToken);
        return await WriteAsync(
            new ConfigurationDocument(normalized, current?.BindUsername, current?.BindUserDistinguishedName),
            expectedETag,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ActiveDirectoryConfigurationSnapshot> ReplaceServiceAccountAsync(
        string username, string? distinguishedName, string? expectedETag, CancellationToken cancellationToken)
    {
        var normalizedUsername = NormalizeUsername(username);
        var normalizedDistinguishedName = NormalizeDistinguishedName(distinguishedName);
        var current = await GetAsync(cancellationToken);
        return await WriteAsync(
            new ConfigurationDocument(current.Domain, normalizedUsername, normalizedDistinguishedName),
            expectedETag,
            cancellationToken);
    }

    /// <summary>Reads the stored override in the current or legacy format.</summary>
    /// <param name="cancellationToken">Cancels the storage request.</param>
    /// <returns>The stored snapshot, or null when no override exists.</returns>
    private async Task<ActiveDirectoryConfigurationSnapshot?> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _blobClient.DownloadContentAsync(cancellationToken);
            using var document = JsonDocument.Parse(response.Value.Content.ToMemory());
            var root = document.RootElement;
            var stored = root.ValueKind == JsonValueKind.String
                ? new ConfigurationDocument(root.GetString(), null, null)
                : root.Deserialize<ConfigurationDocument>(SerializerOptions) ??
                  throw new JsonException("The Active Directory configuration blob is empty.");
            return new ActiveDirectoryConfigurationSnapshot(
                NormalizeDomain(stored.Domain),
                response.Value.Details.ETag.ToString(),
                response.Value.Details.LastModified,
                "blob",
                string.IsNullOrWhiteSpace(stored.BindUsername) ? null : stored.BindUsername,
                stored.BindUserDistinguishedName);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    /// <summary>Writes the complete configuration document conditionally.</summary>
    /// <param name="document">The document to store.</param>
    /// <param name="expectedETag">The expected version; null requires that no blob exists.</param>
    /// <param name="cancellationToken">Cancels the storage request.</param>
    /// <returns>The stored snapshot.</returns>
    private async Task<ActiveDirectoryConfigurationSnapshot> WriteAsync(
        ConfigurationDocument document, string? expectedETag, CancellationToken cancellationToken)
    {
        var response = await _blobClient.UploadAsync(
            BinaryData.FromObjectAsJson(document, SerializerOptions),
            new BlobUploadOptions
            {
                Conditions = expectedETag is null
                    ? new BlobRequestConditions { IfNoneMatch = ETag.All }
                    : new BlobRequestConditions { IfMatch = new ETag(expectedETag) },
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json; charset=utf-8" }
            },
            cancellationToken);
        return new ActiveDirectoryConfigurationSnapshot(
            document.Domain!,
            response.Value.ETag.ToString(),
            response.Value.LastModified,
            "blob",
            document.BindUsername,
            document.BindUserDistinguishedName);
    }

    /// <summary>Validates and normalizes a service-account user principal name.</summary>
    /// <param name="username">The UPN.</param>
    /// <returns>The trimmed UPN.</returns>
    /// <exception cref="ValidationException">The value is not a UPN.</exception>
    public static string NormalizeUsername(string? username)
    {
        var normalized = (username ?? string.Empty).Trim();
        if (normalized.Length > 256 || !Regex.IsMatch(
                normalized,
                @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)))
        {
            throw new ValidationException("Username must be a user principal name, for example svc-tagger@contoso.com.");
        }
        return normalized;
    }

    /// <summary>Validates an optional distinguished name.</summary>
    /// <param name="distinguishedName">The DN or null.</param>
    /// <returns>The trimmed DN, or an empty string to resolve it from the UPN.</returns>
    /// <exception cref="ValidationException">The value is not a DN.</exception>
    public static string NormalizeDistinguishedName(string? distinguishedName)
    {
        var normalized = (distinguishedName ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }
        if (normalized.Length > 1024 || !Regex.IsMatch(
                normalized,
                @"^[A-Za-z][A-Za-z0-9-]*=[^,]+(?:,\s*[A-Za-z][A-Za-z0-9-]*=[^,]+)*$",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)))
        {
            throw new ValidationException("DistinguishedName must be an LDAP distinguished name, for example CN=svc-tagger,OU=Service Accounts,DC=contoso,DC=com.");
        }
        return normalized;
    }

    /// <summary>JSON options for the stored document.</summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The stored configuration document.</summary>
    /// <param name="Domain">The forest DNS domain.</param>
    /// <param name="BindUsername">The service-account UPN override.</param>
    /// <param name="BindUserDistinguishedName">The service-account DN override.</param>
    private sealed record ConfigurationDocument(
        string? Domain,
        string? BindUsername,
        string? BindUserDistinguishedName);

    /// <summary>Normalizes a forest DNS name and rejects malformed management or stored values.</summary>
    /// <param name="domain">The forest DNS name.</param>
    /// <returns>The normalized lowercase DNS name.</returns>
    /// <exception cref="ValidationException">The name is not a DNS domain.</exception>
    public static string NormalizeDomain(string? domain)
    {
        var normalized = (domain ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();
        if (!Regex.IsMatch(
            normalized,
            @"^(?=.{1,253}$)([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1)))
        {
            throw new ValidationException("Domain must be a forest DNS name, for example contoso.com.");
        }
        return normalized;
    }
}
