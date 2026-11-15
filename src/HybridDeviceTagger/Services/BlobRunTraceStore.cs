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

    /// <summary>The append blob that holds the trace of the latest run.</summary>
    private readonly AppendBlobClient _blobClient;

    /// <summary>Logs trace-storage failures, which never fail the tagging run.</summary>
    private readonly ILogger<BlobRunTraceStore> _logger;

    /// <summary>Initializes a new Blob-backed run trace store.</summary>
    /// <param name="blobClient">The append blob at <see cref="BlobName"/>.</param>
    /// <param name="logger">The structured logger.</param>
    public BlobRunTraceStore(
        AppendBlobClient blobClient,
        ILogger<BlobRunTraceStore> logger)
    {
        _blobClient = blobClient;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The previous trace is deleted so the blob only ever contains one run. Storage failures are
    /// logged and yield <see cref="RunTrace.None"/> because diagnostics must not block tagging.
    /// </remarks>
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

    /// <summary>Reads the informational product version recorded in the trace header.</summary>
    /// <returns>The assembly informational version, or <c>unknown</c>.</returns>
    private static string GetProductVersion() =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";

    /// <summary>Appends one JSON line per trace event to the latest-run append blob.</summary>
    /// <remarks>
    /// Each event is appended immediately so the trace survives a crashed or timed-out invocation.
    /// After the first failed append, tracing is disabled for the session to avoid repeated failures.
    /// </remarks>
    private sealed class BlobRunTraceSession : IRunTraceSession
    {
        /// <summary>Compact web-default (camelCase) JSON options producing one line per event.</summary>
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = false
        };

        /// <summary>The append blob created for this run.</summary>
        private readonly AppendBlobClient _blobClient;

        /// <summary>The invocation identifier written into every event.</summary>
        private readonly string _invocationId;

        /// <summary>Logs append failures.</summary>
        private readonly ILogger _logger;

        /// <summary>Serializes appends so sequence numbers match the order of lines in the blob.</summary>
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        /// <summary>The monotonically increasing sequence number of the last written event.</summary>
        private long _sequence;

        /// <summary>Set after an append failure; later writes are ignored.</summary>
        private bool _disabled;

        /// <summary>Initializes a trace session for one invocation.</summary>
        /// <param name="blobClient">The append blob created for this run.</param>
        /// <param name="invocationId">The invocation identifier written into every event.</param>
        /// <param name="logger">The structured logger.</param>
        public BlobRunTraceSession(
            AppendBlobClient blobClient,
            string invocationId,
            ILogger logger)
        {
            _blobClient = blobClient;
            _invocationId = invocationId;
            _logger = logger;
        }

        /// <inheritdoc />
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