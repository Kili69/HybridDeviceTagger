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

using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HybridDeviceTagger.Models;

namespace HybridDeviceTagger.Services;

/// <summary>Stores latest-run statuses in private Azure Blobs.</summary>
public sealed class BlobOperationStatusStore : IOperationStatusStore
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly BlobContainerClient _containerClient;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the Blob-backed status store.</summary>
    public BlobOperationStatusStore(
        BlobContainerClient containerClient,
        TimeProvider timeProvider)
    {
        _containerClient = containerClient;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task StartAsync(
        string operation,
        string invocationId,
        CancellationToken cancellationToken)
    {
        var previous = await GetAsync(operation, cancellationToken);
        await WriteAsync(
            new OperationRunStatus(
                operation,
                invocationId,
                OperationRunState.Running,
                _timeProvider.GetUtcNow(),
                null,
                previous?.LastPasswordChangeAtUtc,
                null,
                null,
                null),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task CompleteAsync(
        string operation,
        string invocationId,
        IReadOnlyDictionary<string, object?> result,
        bool passwordChanged,
        CancellationToken cancellationToken)
    {
        var current = await GetAsync(operation, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        await WriteAsync(
            new OperationRunStatus(
                operation,
                invocationId,
                OperationRunState.Succeeded,
                current?.InvocationId == invocationId ? current.StartedAtUtc : now,
                now,
                passwordChanged ? now : current?.LastPasswordChangeAtUtc,
                result,
                null,
                null),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task FailAsync(
        string operation,
        string invocationId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var current = await GetAsync(operation, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        await WriteAsync(
            new OperationRunStatus(
                operation,
                invocationId,
                OperationRunState.Failed,
                current?.InvocationId == invocationId ? current.StartedAtUtc : now,
                now,
                current?.LastPasswordChangeAtUtc,
                null,
                exception.GetType().FullName,
                exception.Message),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationRunStatus?> GetAsync(
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await GetBlobClient(operation).DownloadContentAsync(cancellationToken);
            return response.Value.Content.ToObjectFromJson<OperationRunStatus>(SerializerOptions);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    private async Task WriteAsync(
        OperationRunStatus status,
        CancellationToken cancellationToken)
    {
        await GetBlobClient(status.Operation).UploadAsync(
            BinaryData.FromObjectAsJson(status, SerializerOptions),
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = "application/json; charset=utf-8"
                }
            },
            cancellationToken);
    }

    private BlobClient GetBlobClient(string operation) => operation switch
    {
        OperationNames.DeviceTagging =>
            _containerClient.GetBlobClient("device-tagger-last-run-status.json"),
        OperationNames.PasswordRotation =>
            _containerClient.GetBlobClient("password-rotation-last-run-status.json"),
        _ => throw new ArgumentOutOfRangeException(
            nameof(operation),
            operation,
            "Unsupported operation status name.")
    };
}