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

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;

namespace HybridDeviceTagger.Services;

/// <summary>Implements cross-instance operation locks with renewable Blob leases.</summary>
/// <remarks>
/// A finite lease is renewed while the operation runs. If the worker stops unexpectedly, the lease
/// expires after <see cref="LeaseDuration"/> and the next run can proceed without manual cleanup.
/// </remarks>
public sealed class BlobOperationLock : IOperationLock
{
    /// <summary>The Blob lease duration; the maximum Storage allows for a finite lease.</summary>
    internal static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);

    /// <summary>The interval between lease renewals, leaving room for two failed renewals per lease.</summary>
    internal static readonly TimeSpan RenewalInterval = TimeSpan.FromSeconds(20);

    /// <summary>The maximum time one renewal call may take before it is abandoned and retried.</summary>
    internal static readonly TimeSpan RenewalTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The time after the last successful acquisition or renewal at which the run is canceled.
    /// It is shorter than <see cref="LeaseDuration"/> so the run stops before another instance can
    /// acquire the expired lease.
    /// </summary>
    internal static readonly TimeSpan LeaseValidityBudget = TimeSpan.FromSeconds(45);

    /// <summary>The private container that holds the lock blobs under <c>locks/</c>.</summary>
    private readonly BlobContainerClient _containerClient;

    /// <summary>The structured logger passed to each lease for renewal diagnostics.</summary>
    private readonly ILogger<BlobOperationLock> _logger;

    /// <summary>Initializes the lock using the private operational Blob container.</summary>
    /// <param name="containerClient">The private container that stores the lock blobs.</param>
    /// <param name="logger">The structured logger.</param>
    public BlobOperationLock(
        BlobContainerClient containerClient,
        ILogger<BlobOperationLock> logger)
    {
        _containerClient = containerClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IOperationLease?> TryAcquireAsync(
        string operation,
        CancellationToken cancellationToken)
    {
        var blobClient = GetBlobClient(operation);
        try
        {
            await blobClient.UploadAsync(
                BinaryData.Empty,
                overwrite: false,
                cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status is 409 or 412)
        {
            // The lock blob already exists, possibly with an active lease.
        }

        var leaseClient = blobClient.GetBlobLeaseClient();
        try
        {
            await leaseClient.AcquireAsync(LeaseDuration, cancellationToken: cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 409)
        {
            return null;
        }

        return new BlobOperationLease(leaseClient, operation, _logger);
    }

    /// <inheritdoc />
    public async Task<bool> IsHeldAsync(string operation, CancellationToken cancellationToken)
    {
        try
        {
            var properties = await GetBlobClient(operation).GetPropertiesAsync(
                cancellationToken: cancellationToken);
            return properties.Value.LeaseState == LeaseState.Leased;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return false;
        }
    }

    /// <summary>Gets the empty lock blob whose lease represents the operation lock.</summary>
    /// <param name="operation">The operation name, validated through <see cref="OperationQueues.For"/>.</param>
    /// <returns>The lock blob client.</returns>
    private BlobClient GetBlobClient(string operation) =>
        _containerClient.GetBlobClient($"locks/{OperationQueues.For(operation)}.lock");

    /// <summary>Represents one held Blob lease that renews itself in the background until disposed.</summary>
    private sealed class BlobOperationLease : IOperationLease
    {
        /// <summary>The client bound to the acquired lease ID.</summary>
        private readonly BlobLeaseClient _leaseClient;

        /// <summary>The operation name used in log messages.</summary>
        private readonly string _operation;

        /// <summary>The structured logger for renewal and release failures.</summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Canceled when the lease is known or presumed lost; doubles as the watchdog timer that is
        /// rearmed after every successful renewal.
        /// </summary>
        private readonly CancellationTokenSource _leaseLost = new();

        /// <summary>Stops the renewal loop when the lease is disposed.</summary>
        private readonly CancellationTokenSource _stopRenewal = new();

        /// <summary>The background renewal loop started by the constructor.</summary>
        private readonly Task _renewal;

        /// <summary>Starts the lease watchdog and the background renewal loop.</summary>
        /// <param name="leaseClient">The client bound to the acquired lease.</param>
        /// <param name="operation">The operation name used in log messages.</param>
        /// <param name="logger">The structured logger.</param>
        public BlobOperationLease(BlobLeaseClient leaseClient, string operation, ILogger logger)
        {
            _leaseClient = leaseClient;
            _operation = operation;
            _logger = logger;
            // Watchdog: the run is canceled before the lease can expire unless a renewal succeeds,
            // even when a renewal call hangs. Each successful renewal restarts this timer.
            _leaseLost.CancelAfter(LeaseValidityBudget);
            _renewal = RenewAsync();
        }

        /// <inheritdoc />
        public CancellationToken LeaseLost => _leaseLost.Token;

        /// <summary>Stops renewal and releases the lease.</summary>
        /// <remarks>
        /// A failed release is only logged because the lease expires automatically after
        /// <see cref="LeaseDuration"/>.
        /// </remarks>
        /// <returns>A task that completes when renewal stopped and the release was attempted.</returns>
        public async ValueTask DisposeAsync()
        {
            await _stopRenewal.CancelAsync();
            await _renewal;
            try
            {
                await _leaseClient.ReleaseAsync();
            }
            catch (RequestFailedException exception)
            {
                _logger.LogWarning(
                    exception,
                    "Unable to release the {Operation} lock; it expires automatically",
                    _operation);
            }

            _stopRenewal.Dispose();
            _leaseLost.Dispose();
        }

        /// <summary>Renews the lease every <see cref="RenewalInterval"/> until disposal.</summary>
        /// <remarks>
        /// A 409 response means another holder owns the lease, so <see cref="LeaseLost"/> is canceled
        /// immediately. Transient failures are retried; if none succeeds within
        /// <see cref="LeaseValidityBudget"/>, the watchdog cancels <see cref="LeaseLost"/>.
        /// </remarks>
        /// <returns>A task that completes when renewal stops.</returns>
        private async Task RenewAsync()
        {
            using var timer = new PeriodicTimer(RenewalInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(_stopRenewal.Token))
                {
                    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(_stopRenewal.Token);
                    attempt.CancelAfter(RenewalTimeout);
                    try
                    {
                        await _leaseClient.RenewAsync(cancellationToken: attempt.Token);
                        if (!_leaseLost.IsCancellationRequested)
                        {
                            _leaseLost.CancelAfter(LeaseValidityBudget);
                        }
                    }
                    catch (RequestFailedException exception) when (exception.Status == 409)
                    {
                        _logger.LogError(
                            exception,
                            "The {Operation} lock was lost; the run is canceled",
                            _operation);
                        await _leaseLost.CancelAsync();
                        return;
                    }
                    catch (Exception exception) when (!_stopRenewal.IsCancellationRequested)
                    {
                        _logger.LogWarning(
                            exception,
                            "Renewal of the {Operation} lock failed; retrying until the lease budget is exhausted",
                            _operation);
                    }
                }
            }
            catch (OperationCanceledException) when (_stopRenewal.IsCancellationRequested)
            {
            }
        }
    }
}
