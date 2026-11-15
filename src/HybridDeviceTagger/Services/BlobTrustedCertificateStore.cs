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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HybridDeviceTagger.Options;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Services;

/// <summary>Describes one LDAP trust anchor without its encoded content.</summary>
/// <param name="Thumbprint">The SHA-1 thumbprint in uppercase hexadecimal.</param>
/// <param name="Subject">The subject distinguished name.</param>
/// <param name="Issuer">The issuer distinguished name.</param>
/// <param name="NotBeforeUtc">The start of the validity period.</param>
/// <param name="NotAfterUtc">The end of the validity period.</param>
/// <param name="IsCertificateAuthority">Whether the basic constraints mark the certificate as a CA.</param>
/// <param name="AddedBy">The principal that added the certificate, or <c>installer</c>.</param>
/// <param name="AddedUtc">When the certificate was added, if known.</param>
public sealed record TrustedCertificateInfo(
    string Thumbprint,
    string Subject,
    string Issuer,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset NotAfterUtc,
    bool IsCertificateAuthority,
    string AddedBy,
    DateTimeOffset? AddedUtc);

/// <summary>Represents the effective, versioned trust-anchor list.</summary>
/// <param name="Certificates">The trust anchors.</param>
/// <param name="ETag">The stored version, or null while the installer package applies.</param>
/// <param name="LastModifiedUtc">The last stored modification time.</param>
/// <param name="Source">
/// <c>blob</c> after a management change, <c>package</c> for the installer certificate, or
/// <c>operatingSystem</c> when no trust anchor is configured.
/// </param>
public sealed record TrustedCertificateSnapshot(
    IReadOnlyList<TrustedCertificateInfo> Certificates,
    string? ETag,
    DateTimeOffset? LastModifiedUtc,
    string Source);

/// <summary>Provides the LDAP server trust anchors and manages them at runtime.</summary>
public interface ITrustedCertificateStore
{
    /// <summary>Gets the trust anchors used for new LDAP connections.</summary>
    /// <param name="cancellationToken">Cancels the storage request.</param>
    /// <returns>The trust anchors, or an empty list to use the operating-system root store.</returns>
    Task<IReadOnlyList<X509Certificate2>> GetTrustAnchorsAsync(CancellationToken cancellationToken);

    /// <summary>Gets the trust-anchor metadata and version.</summary>
    /// <param name="cancellationToken">Cancels the storage request.</param>
    /// <returns>The effective trust anchors.</returns>
    Task<TrustedCertificateSnapshot> GetAsync(CancellationToken cancellationToken);

    /// <summary>Adds a trust anchor.</summary>
    /// <param name="certificate">A PEM or Base64 DER public certificate.</param>
    /// <param name="actor">The principal that adds the certificate.</param>
    /// <param name="expectedETag">The expected version; null when no stored list exists yet.</param>
    /// <param name="cancellationToken">Cancels the storage request.</param>
    /// <returns>The stored trust anchors.</returns>
    /// <exception cref="ValidationException">The certificate is malformed, expired, private, or already trusted.</exception>
    Task<TrustedCertificateSnapshot> AddAsync(
        string certificate, string actor, string? expectedETag, CancellationToken cancellationToken);

    /// <summary>Replaces one trust anchor, for example with a renewed root certificate.</summary>
    /// <param name="thumbprint">The thumbprint of the certificate to replace.</param>
    /// <param name="certificate">A PEM or Base64 DER public certificate.</param>
    /// <param name="actor">The principal that replaces the certificate.</param>
    /// <param name="expectedETag">The expected version; null when no stored list exists yet.</param>
    /// <param name="cancellationToken">Cancels the storage request.</param>
    /// <returns>The stored trust anchors.</returns>
    /// <exception cref="KeyNotFoundException">No trust anchor has the thumbprint.</exception>
    /// <exception cref="ValidationException">The replacement certificate is invalid.</exception>
    Task<TrustedCertificateSnapshot> ReplaceAsync(
        string thumbprint, string certificate, string actor, string? expectedETag, CancellationToken cancellationToken);

    /// <summary>Removes one trust anchor; the last trust anchor cannot be removed.</summary>
    /// <param name="thumbprint">The thumbprint of the certificate to remove.</param>
    /// <param name="expectedETag">The expected version; null when no stored list exists yet.</param>
    /// <param name="cancellationToken">Cancels the storage request.</param>
    /// <returns>The stored trust anchors.</returns>
    /// <exception cref="KeyNotFoundException">No trust anchor has the thumbprint.</exception>
    /// <exception cref="ValidationException">The certificate is the last trust anchor.</exception>
    Task<TrustedCertificateSnapshot> RemoveAsync(
        string thumbprint, string? expectedETag, CancellationToken cancellationToken);
}

/// <summary>Stores LDAP trust anchors in private Blob Storage, seeded from the installer package.</summary>
/// <remarks>
/// Until the first management change, the certificate packaged by the installer applies. The first
/// change copies it into the blob. Only principals with Function App Owner/Contributor access, or
/// with data access to the private Storage account, can change the list; both already control the
/// Function code. Private keys are never accepted or stored.
/// </remarks>
public sealed class BlobTrustedCertificateStore : ITrustedCertificateStore
{
    /// <summary>The stable name of the trust-anchor blob.</summary>
    public const string BlobName = "active-directory-trust-anchors.json";

    /// <summary>The maximum number of trust anchors.</summary>
    public const int MaximumCertificates = 20;

    /// <summary>The actor recorded for the installer-packaged certificate.</summary>
    private const string InstallerActor = "installer";

    /// <summary>JSON options for the stored document.</summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The trust-anchor blob.</summary>
    private readonly BlobClient _blobClient;

    /// <summary>The optional installer-packaged certificate path.</summary>
    private readonly string? _packagedCertificatePath;

    /// <summary>Supplies the time recorded for additions.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the store.</summary>
    /// <param name="blobClient">The trust-anchor blob.</param>
    /// <param name="options">Provides the installer-packaged certificate path.</param>
    /// <param name="timeProvider">Supplies the time recorded for additions.</param>
    public BlobTrustedCertificateStore(
        BlobClient blobClient,
        IOptions<ActiveDirectoryOptions> options,
        TimeProvider timeProvider)
    {
        _blobClient = blobClient;
        _packagedCertificatePath = options.Value.TrustedCertificatePath;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<X509Certificate2>> GetTrustAnchorsAsync(CancellationToken cancellationToken)
    {
        var (entries, _, _, _) = await LoadAsync(cancellationToken);
        return [.. entries.Select(entry => entry.Certificate)];
    }

    /// <inheritdoc />
    public async Task<TrustedCertificateSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        var (entries, eTag, lastModified, source) = await LoadAsync(cancellationToken);
        return ToSnapshot(entries, eTag, lastModified, source);
    }

    /// <inheritdoc />
    public async Task<TrustedCertificateSnapshot> AddAsync(
        string certificate, string actor, string? expectedETag, CancellationToken cancellationToken)
    {
        var parsed = ParseCertificate(certificate);
        var (entries, _, _, _) = await LoadAsync(cancellationToken);
        if (entries.Any(entry => entry.Certificate.Thumbprint == parsed.Thumbprint))
        {
            throw new ValidationException($"The certificate '{parsed.Thumbprint}' is already trusted.");
        }
        if (entries.Count >= MaximumCertificates)
        {
            throw new ValidationException($"At most {MaximumCertificates} trust anchors are supported.");
        }

        entries.Add(new TrustAnchor(parsed, actor, _timeProvider.GetUtcNow()));
        return await WriteAsync(entries, expectedETag, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TrustedCertificateSnapshot> ReplaceAsync(
        string thumbprint, string certificate, string actor, string? expectedETag, CancellationToken cancellationToken)
    {
        var parsed = ParseCertificate(certificate);
        var (entries, _, _, _) = await LoadAsync(cancellationToken);
        var index = FindIndex(entries, thumbprint);
        if (entries.Where((_, position) => position != index)
            .Any(entry => entry.Certificate.Thumbprint == parsed.Thumbprint))
        {
            throw new ValidationException($"The certificate '{parsed.Thumbprint}' is already trusted.");
        }

        entries[index] = new TrustAnchor(parsed, actor, _timeProvider.GetUtcNow());
        return await WriteAsync(entries, expectedETag, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TrustedCertificateSnapshot> RemoveAsync(
        string thumbprint, string? expectedETag, CancellationToken cancellationToken)
    {
        var (entries, _, _, _) = await LoadAsync(cancellationToken);
        var index = FindIndex(entries, thumbprint);
        if (entries.Count == 1)
        {
            throw new ValidationException(
                "The last trust anchor cannot be removed. Add the replacement certificate first or replace this certificate.");
        }

        entries.RemoveAt(index);
        return await WriteAsync(entries, expectedETag, cancellationToken);
    }

    /// <summary>Parses one public certificate from PEM or Base64 DER text.</summary>
    /// <param name="text">The certificate text.</param>
    /// <returns>The parsed certificate without a private key.</returns>
    /// <exception cref="ValidationException">The text is not exactly one valid, unexpired public certificate.</exception>
    internal X509Certificate2 ParseCertificate(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            throw new ValidationException("A certificate is required.");
        }
        if (value.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("Upload only the public certificate. Private keys are never accepted.");
        }

        X509Certificate2 certificate;
        try
        {
            if (value.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal))
            {
                var collection = new X509Certificate2Collection();
                collection.ImportFromPem(value);
                if (collection.Count != 1)
                {
                    throw new ValidationException("Upload exactly one certificate per request.");
                }
                certificate = collection[0];
            }
            else
            {
                certificate = new X509Certificate2(Convert.FromBase64String(value));
            }
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            throw new ValidationException("The certificate is not a valid PEM or DER (.cer) X.509 certificate.");
        }

        if (certificate.HasPrivateKey)
        {
            throw new ValidationException("Upload only the public certificate. Private keys are never accepted.");
        }
        if (certificate.NotAfter.ToUniversalTime() <= _timeProvider.GetUtcNow().UtcDateTime)
        {
            throw new ValidationException($"The certificate '{certificate.Thumbprint}' has expired.");
        }

        return new X509Certificate2(certificate.Export(X509ContentType.Cert));
    }

    /// <summary>Loads the stored list, or the installer-packaged certificate when none is stored.</summary>
    /// <param name="cancellationToken">Cancels the storage request.</param>
    /// <returns>The entries, version, modification time, and source.</returns>
    private async Task<(List<TrustAnchor> Entries, string? ETag, DateTimeOffset? LastModified, string Source)> LoadAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _blobClient.DownloadContentAsync(cancellationToken);
            var document = response.Value.Content.ToObjectFromJson<StoredDocument>(SerializerOptions) ??
                throw new JsonException("The trust-anchor blob is empty.");
            var entries = document.Certificates.Select(stored =>
            {
                var collection = new X509Certificate2Collection();
                collection.ImportFromPem(stored.Pem);
                return new TrustAnchor(collection[0], stored.AddedBy, stored.AddedUtc);
            }).ToList();
            return (entries, response.Value.Details.ETag.ToString(), response.Value.Details.LastModified, "blob");
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            var packaged = ActiveDirectoryLdapConnectionFactory.LoadTrustedCertificates(_packagedCertificatePath);
            return (
                [.. packaged.Select(certificate => new TrustAnchor(certificate, InstallerActor, null))],
                null,
                null,
                packaged.Count > 0 ? "package" : "operatingSystem");
        }
    }

    /// <summary>Writes the complete list conditionally.</summary>
    /// <param name="entries">The trust anchors.</param>
    /// <param name="expectedETag">The expected version; null requires that no blob exists.</param>
    /// <param name="cancellationToken">Cancels the storage request.</param>
    /// <returns>The stored snapshot.</returns>
    private async Task<TrustedCertificateSnapshot> WriteAsync(
        List<TrustAnchor> entries, string? expectedETag, CancellationToken cancellationToken)
    {
        var document = new StoredDocument([.. entries.Select(entry => new StoredCertificate(
            entry.Certificate.ExportCertificatePem(), entry.AddedBy, entry.AddedUtc))]);
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
        return ToSnapshot(entries, response.Value.ETag.ToString(), response.Value.LastModified, "blob");
    }

    /// <summary>Finds a trust anchor by thumbprint, ignoring case and separators.</summary>
    /// <param name="entries">The trust anchors.</param>
    /// <param name="thumbprint">The thumbprint.</param>
    /// <returns>The index of the trust anchor.</returns>
    /// <exception cref="KeyNotFoundException">No trust anchor has the thumbprint.</exception>
    private static int FindIndex(List<TrustAnchor> entries, string thumbprint)
    {
        var normalized = new string((thumbprint ?? string.Empty).Where(char.IsAsciiHexDigit).ToArray());
        var index = entries.FindIndex(entry =>
            string.Equals(entry.Certificate.Thumbprint, normalized, StringComparison.OrdinalIgnoreCase));
        return index >= 0
            ? index
            : throw new KeyNotFoundException($"No trust anchor with thumbprint '{thumbprint}' exists.");
    }

    /// <summary>Creates the metadata snapshot.</summary>
    /// <param name="entries">The trust anchors.</param>
    /// <param name="eTag">The stored version.</param>
    /// <param name="lastModified">The last modification time.</param>
    /// <param name="source">The source.</param>
    /// <returns>The snapshot.</returns>
    private static TrustedCertificateSnapshot ToSnapshot(
        List<TrustAnchor> entries, string? eTag, DateTimeOffset? lastModified, string source) =>
        new([.. entries.Select(entry => new TrustedCertificateInfo(
                entry.Certificate.Thumbprint,
                entry.Certificate.Subject,
                entry.Certificate.Issuer,
                new DateTimeOffset(entry.Certificate.NotBefore.ToUniversalTime()),
                new DateTimeOffset(entry.Certificate.NotAfter.ToUniversalTime()),
                entry.Certificate.Extensions.OfType<X509BasicConstraintsExtension>()
                    .Any(extension => extension.CertificateAuthority),
                entry.AddedBy,
                entry.AddedUtc))],
            eTag,
            lastModified,
            source);

    /// <summary>One trust anchor with its audit metadata.</summary>
    /// <param name="Certificate">The public certificate.</param>
    /// <param name="AddedBy">The principal that added it.</param>
    /// <param name="AddedUtc">When it was added.</param>
    private sealed record TrustAnchor(X509Certificate2 Certificate, string AddedBy, DateTimeOffset? AddedUtc);

    /// <summary>The stored document.</summary>
    /// <param name="Certificates">The stored certificates.</param>
    private sealed record StoredDocument(IReadOnlyList<StoredCertificate> Certificates);

    /// <summary>One stored certificate.</summary>
    /// <param name="Pem">The PEM-encoded public certificate.</param>
    /// <param name="AddedBy">The principal that added it.</param>
    /// <param name="AddedUtc">When it was added.</param>
    private sealed record StoredCertificate(string Pem, string AddedBy, DateTimeOffset? AddedUtc);
}
