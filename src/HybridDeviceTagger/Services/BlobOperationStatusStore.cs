/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
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
    /// <summary>Web-default (camelCase) JSON options used for the status documents.</summary>
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    /// <summary>The private container that holds one status blob per operation.</summary>
    private readonly BlobContainerClient _containerClient;

    /// <summary>Supplies the UTC timestamps recorded in status documents.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the Blob-backed status store.</summary>
    /// <remarks>
    /// Each write replaces the operation's single status blob (last writer wins); the operation lock
    /// ensures only one run of an operation writes its status at a time.
    /// </remarks>
    /// <param name="containerClient">The private container that stores the status blobs.</param>
    /// <param name="timeProvider">The clock used for status timestamps.</param>
    public BlobOperationStatusStore(
        BlobContainerClient containerClient,
        TimeProvider timeProvider)
    {
        _containerClient = containerClient;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task QueueAsync(
        string operation,
        string invocationId,
        CancellationToken cancellationToken)
    {
        var previous = await GetAsync(operation, cancellationToken);
        await WriteAsync(
            new OperationRunStatus(
                operation,
                invocationId,
                OperationRunState.Queued,
                _timeProvider.GetUtcNow(),
                null,
                previous?.LastPasswordChangeAtUtc,
                null,
                null,
                null),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task RejectQueuedAsync(
        string operation,
        string invocationId,
        Exception reason,
        CancellationToken cancellationToken)
    {
        var current = await GetAsync(operation, cancellationToken);
        if (current is null ||
            current.InvocationId != invocationId ||
            current.State != OperationRunState.Queued)
        {
            return;
        }

        await WriteAsync(
            current with
            {
                State = OperationRunState.Failed,
                CompletedAtUtc = _timeProvider.GetUtcNow(),
                ErrorType = reason.GetType().FullName,
                ErrorMessage = reason.Message
            },
            cancellationToken);
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

    /// <summary>Uploads a status document, overwriting the operation's previous status.</summary>
    /// <param name="status">The status to persist.</param>
    /// <param name="cancellationToken">Cancels the upload.</param>
    /// <returns>A task representing the asynchronous upload.</returns>
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

    /// <summary>Maps a known operation name to its fixed status blob.</summary>
    /// <remarks>Using a fixed allow-list prevents caller-supplied names from addressing arbitrary blobs.</remarks>
    /// <param name="operation">The operation name.</param>
    /// <returns>The blob client for the operation's status document.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The operation name is not supported.</exception>
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