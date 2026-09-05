/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using System.Reflection;
using System.Text.Json;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;

namespace HybridDeviceTagger.Services;

/// <summary>Stores the latest tagging invocation as immediately persisted JSON Lines.</summary>
public sealed class BlobRunTraceStore : IRunTraceStore, IRunTraceReader
{
    /// <summary>The stable blob path containing only the latest tagging run.</summary>
    public const string BlobName = "device-tagger-last-run.jsonl";

    private readonly AppendBlobClient _blobClient;
    private readonly ILogger<BlobRunTraceStore> _logger;

    /// <summary>Initializes a new Blob-backed run trace store.</summary>
    public BlobRunTraceStore(
        AppendBlobClient blobClient,
        ILogger<BlobRunTraceStore> logger)
    {
        _blobClient = blobClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IRunTraceSession> StartAsync(
        string invocationId,
        bool isPastDue,
        CancellationToken cancellationToken)
    {
        try
        {
            await _blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
            await _blobClient.CreateAsync(
                new AppendBlobCreateOptions
                {
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = "application/x-ndjson; charset=utf-8"
                    }
                },
                cancellationToken);

            var session = new BlobRunTraceSession(_blobClient, invocationId, _logger);
            await session.WriteAsync(
                "Information",
                "Invocation",
                "Start",
                "Started",
                new Dictionary<string, object?>
                {
                    ["schemaVersion"] = 1,
                    ["productVersion"] = GetProductVersion(),
                    ["isPastDue"] = isPastDue
                },
                null,
                cancellationToken);
            return session;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "Unable to initialize the latest-run diagnostic trace blob {TraceBlobName}",
                BlobName);
            return RunTrace.None;
        }
    }

    /// <inheritdoc />
    public async Task<RunTraceDownload?> OpenLatestAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _blobClient.DownloadStreamingAsync(
                cancellationToken: cancellationToken);
            return new RunTraceDownload(
                response.Value.Content,
                response.Value.Details.ContentType ?? "application/x-ndjson; charset=utf-8",
                BlobName);
        }
        catch (Azure.RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    private static string GetProductVersion() =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";

    private sealed class BlobRunTraceSession : IRunTraceSession
    {
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = false
        };

        private readonly AppendBlobClient _blobClient;
        private readonly string _invocationId;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private long _sequence;
        private bool _disabled;

        public BlobRunTraceSession(
            AppendBlobClient blobClient,
            string invocationId,
            ILogger logger)
        {
            _blobClient = blobClient;
            _invocationId = invocationId;
            _logger = logger;
        }

        public async Task WriteAsync(
            string level,
            string phase,
            string action,
            string outcome,
            IReadOnlyDictionary<string, object?>? details,
            Exception? exception,
            CancellationToken cancellationToken)
        {
            if (_disabled)
            {
                return;
            }

            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                var traceEvent = new
                {
                    timestampUtc = DateTimeOffset.UtcNow,
                    sequence = ++_sequence,
                    invocationId = _invocationId,
                    level,
                    phase,
                    action,
                    outcome,
                    details,
                    exception = exception is null
                        ? null
                        : new
                        {
                            type = exception.GetType().FullName,
                            exception.Message,
                            exception.StackTrace,
                            innerType = exception.InnerException?.GetType().FullName,
                            innerMessage = exception.InnerException?.Message
                        }
                };
                var line = JsonSerializer.Serialize(traceEvent, SerializerOptions) + "\n";
                using var content = BinaryData.FromString(line).ToStream();
                await _blobClient.AppendBlockAsync(
                    content,
                    cancellationToken: cancellationToken);
            }
            catch (Exception writeException) when (writeException is not OperationCanceledException)
            {
                _disabled = true;
                _logger.LogWarning(
                    writeException,
                    "Unable to append to the latest-run diagnostic trace blob {TraceBlobName}; tracing is disabled for this invocation",
                    BlobName);
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }
}