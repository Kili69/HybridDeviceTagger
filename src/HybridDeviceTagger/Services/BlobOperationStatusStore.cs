/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
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