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
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HybridDeviceTagger.Options;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Services;

/// <summary>Represents the effective, versioned operation intervals.</summary>
/// <param name="TaggingIntervalMinutes">Minutes between two scheduled tagging runs.</param>
/// <param name="PasswordRotationDays">Password age in days at which rotation is due.</param>
/// <param name="MaximumPasswordRotationDays">The largest accepted rotation interval.</param>
/// <param name="ETag">The stored version, or null while the application settings apply.</param>
/// <param name="LastModifiedUtc">The last stored modification time.</param>
/// <param name="Source"><c>blob</c> after a management change; otherwise <c>appSetting</c>.</param>
public sealed record OperationScheduleSnapshot(
    int TaggingIntervalMinutes,
    int PasswordRotationDays,
    int MaximumPasswordRotationDays,
    string? ETag,
    DateTimeOffset? LastModifiedUtc,
    string Source);

/// <summary>Reads and updates the tagging and password-rotation intervals at runtime.</summary>
public interface IOperationScheduleStore
{
    /// <summary>Gets the stored intervals or the initial application settings.</summary>
    /// <param name="cancellationToken">Cancels the storage request.</param>
    /// <returns>The effective intervals.</returns>
    Task<OperationScheduleSnapshot> GetAsync(CancellationToken cancellationToken);

    /// <summary>Validates and stores both intervals.</summary>
    /// <param name="taggingIntervalMinutes">Minutes between scheduled tagging runs.</param>
    /// <param name="passwordRotationDays">Password age in days at which rotation is due.</param>
    /// <param name="expectedETag">The expected version; null when no stored version exists.</param>
    /// <param name="cancellationToken">Cancels the storage request.</param>
    /// <returns>The stored intervals.</returns>
    /// <exception cref="ValidationException">An interval is outside its supported range.</exception>
    Task<OperationScheduleSnapshot> ReplaceAsync(
        int taggingIntervalMinutes,
        int passwordRotationDays,
        string? expectedETag,
        CancellationToken cancellationToken);
}

/// <summary>Persists the operation intervals in private Blob Storage, shared by every Function instance.</summary>
/// <remarks>
/// Timers tick at fixed short rates and consult these intervals, so changes apply to the next
/// tick without restarting the Function and survive reinstallation.
/// </remarks>
public sealed class BlobOperationScheduleStore : IOperationScheduleStore
{
    /// <summary>The stable name of the schedule blob.</summary>
    public const string BlobName = "operation-schedule.json";

    /// <summary>JSON options for the stored document.</summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The schedule blob.</summary>
    private readonly BlobClient _blobClient;

    /// <summary>The initial tagging interval.</summary>
    private readonly int _initialTaggingIntervalMinutes;

    /// <summary>The initial rotation interval.</summary>
    private readonly int _initialPasswordRotationDays;

    /// <summary>The largest rotation interval that keeps staged secrets valid for recovery.</summary>
    private readonly int _maximumPasswordRotationDays;

    /// <summary>Initializes the store.</summary>
    /// <param name="blobClient">The schedule blob.</param>
    /// <param name="taggingOptions">Provides the initial tagging interval.</param>
    /// <param name="rotationOptions">Provides the initial rotation interval and secret lifetime.</param>
    public BlobOperationScheduleStore(
        BlobClient blobClient,
        IOptions<TaggingOptions> taggingOptions,
        IOptions<PasswordRotationOptions> rotationOptions)
    {
        _blobClient = blobClient;
        _initialTaggingIntervalMinutes = taggingOptions.Value.IntervalMinutes;
        _initialPasswordRotationDays = rotationOptions.Value.RotateAfterDays;
        _maximumPasswordRotationDays = Math.Min(
            PasswordRotationOptions.MaximumRotateAfterDays,
            rotationOptions.Value.SecretExpiryDays - 1);
    }

    /// <inheritdoc />
    public async Task<OperationScheduleSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _blobClient.DownloadContentAsync(cancellationToken);
            var document = response.Value.Content.ToObjectFromJson<StoredSchedule>(SerializerOptions) ??
                throw new JsonException("The operation schedule blob is empty.");
            return new OperationScheduleSnapshot(
                Math.Clamp(document.TaggingIntervalMinutes, TaggingOptions.MinimumIntervalMinutes, TaggingOptions.MaximumIntervalMinutes),
                Math.Clamp(document.PasswordRotationDays, 1, _maximumPasswordRotationDays),
                _maximumPasswordRotationDays,
                response.Value.Details.ETag.ToString(),
                response.Value.Details.LastModified,
                "blob");
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return new OperationScheduleSnapshot(
                _initialTaggingIntervalMinutes,
                Math.Min(_initialPasswordRotationDays, _maximumPasswordRotationDays),
                _maximumPasswordRotationDays,
                null,
                null,
                "appSetting");
        }
    }

    /// <inheritdoc />
    public async Task<OperationScheduleSnapshot> ReplaceAsync(
        int taggingIntervalMinutes,
        int passwordRotationDays,
        string? expectedETag,
        CancellationToken cancellationToken)
    {
        if (taggingIntervalMinutes is < TaggingOptions.MinimumIntervalMinutes or > TaggingOptions.MaximumIntervalMinutes)
        {
            throw new ValidationException(
                $"The tagging interval must be between {TaggingOptions.MinimumIntervalMinutes} and {TaggingOptions.MaximumIntervalMinutes} minutes.");
        }
        if (passwordRotationDays < 1 || passwordRotationDays > _maximumPasswordRotationDays)
        {
            throw new ValidationException(
                $"The password rotation interval must be between 1 and {_maximumPasswordRotationDays} days.");
        }

        var response = await _blobClient.UploadAsync(
            BinaryData.FromObjectAsJson(new StoredSchedule(taggingIntervalMinutes, passwordRotationDays), SerializerOptions),
            new BlobUploadOptions
            {
                Conditions = expectedETag is null
                    ? new BlobRequestConditions { IfNoneMatch = ETag.All }
                    : new BlobRequestConditions { IfMatch = new ETag(expectedETag) },
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json; charset=utf-8" }
            },
            cancellationToken);
        return new OperationScheduleSnapshot(
            taggingIntervalMinutes,
            passwordRotationDays,
            _maximumPasswordRotationDays,
            response.Value.ETag.ToString(),
            response.Value.LastModified,
            "blob");
    }

    /// <summary>The stored document.</summary>
    /// <param name="TaggingIntervalMinutes">Minutes between scheduled tagging runs.</param>
    /// <param name="PasswordRotationDays">Password age in days at which rotation is due.</param>
    private sealed record StoredSchedule(int TaggingIntervalMinutes, int PasswordRotationDays);
}
