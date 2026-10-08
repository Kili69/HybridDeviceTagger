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

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HybridDeviceTagger.Options;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Services;

/// <summary>Describes a public certificate trusted for Active Directory LDAPS.</summary>
public sealed record TrustedCertificateMetadata(
    string Thumbprint,
    string Subject,
    string Issuer,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset NotAfterUtc);

/// <summary>Represents the effective LDAPS trust-certificate collection.</summary>
public sealed record TrustedCertificateSnapshot(
    IReadOnlyList<TrustedCertificateMetadata> Certificates,
    string? ETag,
    DateTimeOffset? LastModifiedUtc,
    string Source);

/// <summary>Provides current LDAPS trust anchors and management operations.</summary>
public interface IActiveDirectoryTrustStore
{
    /// <summary>Loads public certificates used by new LDAP connections.</summary>
    IReadOnlyList<X509Certificate2> GetCertificates();

    /// <summary>Gets non-sensitive metadata for the effective certificate set.</summary>
    Task<TrustedCertificateSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);

    /// <summary>Adds one public certificate to the effective certificate set.</summary>
    Task<TrustedCertificateSnapshot> AddAsync(
        byte[] certificateBytes,
        string? expectedETag,
        CancellationToken cancellationToken);

    /// <summary>Removes one certificate by SHA-1 thumbprint.</summary>
    Task<TrustedCertificateSnapshot> RemoveAsync(
        string thumbprint,
        string? expectedETag,
        CancellationToken cancellationToken);
}

/// <summary>Stores the dynamic LDAPS trust-certificate set in a private Blob.</summary>
public sealed class BlobActiveDirectoryTrustStore : IActiveDirectoryTrustStore
{
    /// <summary>The stable Blob name containing public LDAPS trust certificates.</summary>
    public const string BlobName = "active-directory-trust-certificates.json";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly BlobClient _blobClient;
    private readonly byte[]? _fallbackCertificate;

    /// <summary>Initializes the trust store with the packaged bootstrap certificate.</summary>
    public BlobActiveDirectoryTrustStore(
        BlobClient blobClient,
        IOptions<ActiveDirectoryOptions> options)
    {
        _blobClient = blobClient;
        if (string.IsNullOrWhiteSpace(options.Value.TrustedCertificatePath))
        {
            return;
        }

        var path = Path.GetFullPath(options.Value.TrustedCertificatePath);
        using var fallbackCertificate = LoadCertificate(File.ReadAllBytes(path));
        _fallbackCertificate = fallbackCertificate.Export(X509ContentType.Cert);
    }

    /// <inheritdoc />
    public IReadOnlyList<X509Certificate2> GetCertificates()
    {
        try
        {
            var response = _blobClient.DownloadContent();
            return ParseDocument(response.Value.Content);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return CreateFallbackCertificates();
        }
    }

    /// <inheritdoc />
    public async Task<TrustedCertificateSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _blobClient.DownloadContentAsync(cancellationToken);
            return CreateSnapshot(
                ParseDocument(response.Value.Content),
                response.Value.Details.ETag.ToString(),
                response.Value.Details.LastModified,
                "blob");
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return CreateSnapshot(
                CreateFallbackCertificates(),
                null,
                null,
                _fallbackCertificate is null ? "none" : "package");
        }
    }

    /// <inheritdoc />
    public async Task<TrustedCertificateSnapshot> AddAsync(
        byte[] certificateBytes,
        string? expectedETag,
        CancellationToken cancellationToken)
    {
        using var certificate = LoadCertificate(certificateBytes);
        if (certificate.HasPrivateKey)
        {
            throw new TrustedCertificateValidationException("Certificates containing a private key are not accepted.");
        }

        var snapshot = await GetSnapshotAsync(cancellationToken);
        var certificates = GetCertificatesForMutation(snapshot.Source);
        if (certificates.Any(existing => string.Equals(
            existing.Thumbprint,
            certificate.Thumbprint,
            StringComparison.OrdinalIgnoreCase)))
        {
            return snapshot;
        }

        certificates.Add(new X509Certificate2(certificate.Export(X509ContentType.Cert)));
        return await UploadAsync(certificates, expectedETag, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TrustedCertificateSnapshot> RemoveAsync(
        string thumbprint,
        string? expectedETag,
        CancellationToken cancellationToken)
    {
        var normalizedThumbprint = NormalizeThumbprint(thumbprint);
        var snapshot = await GetSnapshotAsync(cancellationToken);
        var certificates = GetCertificatesForMutation(snapshot.Source);
        var removed = certificates.RemoveAll(certificate => string.Equals(
            certificate.Thumbprint,
            normalizedThumbprint,
            StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            throw new KeyNotFoundException($"Trusted certificate '{normalizedThumbprint}' was not found.");
        }
        if (certificates.Count == 0)
        {
            throw new TrustedCertificateValidationException(
                "At least one Active Directory trust certificate must remain configured.");
        }

        return await UploadAsync(certificates, expectedETag, cancellationToken);
    }

    private List<X509Certificate2> GetCertificatesForMutation(string source) =>
        source == "blob"
            ? GetCertificates().ToList()
            : CreateFallbackCertificates().ToList();

    private IReadOnlyList<X509Certificate2> CreateFallbackCertificates() =>
        _fallbackCertificate is null
            ? []
            : [new X509Certificate2(_fallbackCertificate)];

    private async Task<TrustedCertificateSnapshot> UploadAsync(
        IReadOnlyList<X509Certificate2> certificates,
        string? expectedETag,
        CancellationToken cancellationToken)
    {
        var conditions = expectedETag is null
            ? new BlobRequestConditions { IfNoneMatch = ETag.All }
            : new BlobRequestConditions { IfMatch = new ETag(expectedETag) };
        var document = certificates
            .Select(certificate => Convert.ToBase64String(certificate.Export(X509ContentType.Cert)))
            .ToArray();

        try
        {
            var response = await _blobClient.UploadAsync(
                BinaryData.FromObjectAsJson(document, SerializerOptions),
                new BlobUploadOptions
                {
                    Conditions = conditions,
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/json; charset=utf-8" }
                },
                cancellationToken);
            return CreateSnapshot(certificates, response.Value.ETag.ToString(), response.Value.LastModified, "blob");
        }
        catch (RequestFailedException exception) when (exception.Status is 409 or 412)
        {
            throw new TrustedCertificateConflictException(exception);
        }
    }

    private static IReadOnlyList<X509Certificate2> ParseDocument(BinaryData content)
    {
        var encodedCertificates = content.ToObjectFromJson<string[]>(SerializerOptions) ?? [];
        if (encodedCertificates.Length == 0)
        {
            throw new TrustedCertificateValidationException(
                "The stored Active Directory trust-certificate collection is empty.");
        }

        return encodedCertificates
            .Select(encoded => new X509Certificate2(Convert.FromBase64String(encoded)))
            .ToArray();
    }

    private static X509Certificate2 LoadCertificate(byte[] bytes)
    {
        try
        {
            var text = Encoding.UTF8.GetString(bytes);
            return text.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal)
                ? X509Certificate2.CreateFromPem(text)
                : new X509Certificate2(bytes);
        }
        catch (CryptographicException exception)
        {
            throw new TrustedCertificateValidationException(
                "The request does not contain a valid DER or PEM X.509 certificate.",
                exception);
        }
    }

    private static TrustedCertificateSnapshot CreateSnapshot(
        IReadOnlyList<X509Certificate2> certificates,
        string? etag,
        DateTimeOffset? lastModifiedUtc,
        string source) =>
        new(
            certificates
                .OrderBy(certificate => certificate.Thumbprint, StringComparer.OrdinalIgnoreCase)
                .Select(certificate => new TrustedCertificateMetadata(
                    certificate.Thumbprint,
                    certificate.Subject,
                    certificate.Issuer,
                    certificate.NotBefore.ToUniversalTime(),
                    certificate.NotAfter.ToUniversalTime()))
                .ToArray(),
            etag,
            lastModifiedUtc,
            source);

    private static string NormalizeThumbprint(string thumbprint)
    {
        var normalized = new string(thumbprint.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        if (normalized.Length != 40)
        {
            throw new TrustedCertificateValidationException(
                "Certificate thumbprints must contain exactly 40 hexadecimal characters.");
        }
        return normalized;
    }
}

/// <summary>Indicates invalid certificate input or an unsafe resulting trust collection.</summary>
public sealed class TrustedCertificateValidationException : Exception
{
    /// <summary>Initializes the validation error.</summary>
    public TrustedCertificateValidationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>Indicates that the certificate collection changed concurrently.</summary>
public sealed class TrustedCertificateConflictException : Exception
{
    /// <summary>Initializes the concurrency conflict.</summary>
    public TrustedCertificateConflictException(Exception innerException)
        : base("The trusted-certificate collection changed. Read it again before retrying.", innerException)
    {
    }
}