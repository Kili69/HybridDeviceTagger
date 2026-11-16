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

using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using HybridDeviceTagger.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger;

/// <summary>Runs the scheduled and requested hybrid-device tagging workflow.</summary>
public class DeviceTaggerFunction
{
    /// <summary>The orchestrator that discovers, tags, and reconciles hybrid devices.</summary>
    private readonly DeviceTaggingService _deviceTaggingService;

    /// <summary>Creates the per-invocation diagnostic trace session.</summary>
    private readonly IRunTraceStore _runTraceStore;

    /// <summary>Persists the latest-run status shown by the management API.</summary>
    private readonly IOperationStatusStore _statusStore;

    /// <summary>Prevents concurrent device-tagging runs across Function instances.</summary>
    private readonly IOperationLock _operationLock;

    /// <summary>The structured logger for invocation-level events.</summary>
    private readonly ILogger<DeviceTaggerFunction> _logger;

    /// <summary>Records run outcomes in the activity log.</summary>
    private readonly IActivityLog _activityLog;

    /// <summary>The fixed timer rate, equal to the shortest configurable tagging interval.</summary>
    internal const string TimerSchedule = "0 */5 * * * *";

    /// <summary>Tolerance for timer jitter, so a run started just after a tick does not skip a whole tick.</summary>
    private static readonly TimeSpan DueTolerance = TimeSpan.FromMinutes(1);

    /// <summary>Supplies the configured tagging interval; null runs on every tick.</summary>
    private readonly IOperationScheduleStore? _scheduleStore;

    /// <summary>Supplies the current time for the interval check.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>Supplies the post-installation directory configuration; null always runs.</summary>
    private readonly IActiveDirectoryConfigurationStore? _directoryConfigurationStore;

    /// <summary>The initial directory application settings.</summary>
    private readonly ActiveDirectoryOptions _directoryOptions;

    /// <summary>Initializes a new instance of the <see cref="DeviceTaggerFunction"/> class.</summary>
    /// <param name="deviceTaggingService">The device-tagging orchestrator.</param>
    /// <param name="runTraceStore">The latest-run diagnostic trace store.</param>
    /// <param name="statusStore">The latest-run status store.</param>
    /// <param name="operationLock">The cross-instance operation lock.</param>
    /// <param name="logger">The structured logger.</param>
    /// <param name="activityLog">The optional activity log.</param>
    /// <param name="scheduleStore">The optional runtime interval store; without it every timer tick runs.</param>
    /// <param name="timeProvider">The optional time source.</param>
    /// <param name="directoryConfigurationStore">
    /// The optional directory configuration; scheduled runs are skipped while it is incomplete.
    /// </param>
    /// <param name="directoryOptions">The optional initial directory application settings.</param>
    public DeviceTaggerFunction(
        DeviceTaggingService deviceTaggingService,
        IRunTraceStore runTraceStore,
        IOperationStatusStore statusStore,
        IOperationLock operationLock,
        ILogger<DeviceTaggerFunction> logger,
        IActivityLog? activityLog = null,
        IOperationScheduleStore? scheduleStore = null,
        TimeProvider? timeProvider = null,
        IActiveDirectoryConfigurationStore? directoryConfigurationStore = null,
        IOptions<ActiveDirectoryOptions>? directoryOptions = null)
    {
        _scheduleStore = scheduleStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _directoryConfigurationStore = directoryConfigurationStore;
        _directoryOptions = directoryOptions?.Value ?? new ActiveDirectoryOptions();
        _activityLog = activityLog ?? NullActivityLog.Instance;
        _deviceTaggingService = deviceTaggingService;
        _runTraceStore = runTraceStore;
        _statusStore = statusStore;
        _operationLock = operationLock;
        _logger = logger;
    }

    /// <summary>Tags eligible devices when the configured interval has elapsed.</summary>
    /// <remarks>
    /// The timer ticks every five minutes; the configurable interval decides whether a tick runs.
    /// Ticks are skipped until the Active Directory forest and service account are configured.
    /// </remarks>
    /// <param name="timer">Information about the timer invocation.</param>
    /// <param name="context">The Functions invocation context used for log correlation.</param>
    /// <param name="cancellationToken">Cancels the invocation.</param>
    /// <returns>A task representing the asynchronous invocation.</returns>
    [Function("DeviceTaggerFunction")]
    public async Task RunAsync(
        [TimerTrigger(TimerSchedule)] TimerInfo timer,
        FunctionContext context,
        CancellationToken cancellationToken)
    {
        if (await IsDirectoryConfiguredAsync(cancellationToken) && await IsDueAsync(cancellationToken))
        {
            await RunOperationAsync(context.InvocationId, timer.IsPastDue, false, cancellationToken);
        }
    }

    /// <summary>Returns whether AD access is configured and logs the missing setting otherwise.</summary>
    /// <param name="cancellationToken">Cancels the storage read.</param>
    /// <returns><see langword="true"/> when no configuration store is used or it is complete.</returns>
    internal async Task<bool> IsDirectoryConfiguredAsync(CancellationToken cancellationToken)
    {
        if (_directoryConfigurationStore is null)
        {
            return true;
        }

        var missing = (await _directoryConfigurationStore.GetAsync(cancellationToken))
            .GetMissingConfiguration(_directoryOptions);
        if (missing is null)
        {
            return true;
        }

        _logger.LogInformation(
            "Scheduled device tagging skipped because the Active Directory {MissingSetting} is not configured",
            missing);
        return false;
    }

    /// <summary>Returns whether the configured interval has elapsed since the last run started.</summary>
    /// <param name="cancellationToken">Cancels the storage reads.</param>
    /// <returns><see langword="true"/> when no run is recorded or the interval has elapsed.</returns>
    internal async Task<bool> IsDueAsync(CancellationToken cancellationToken)
    {
        if (_scheduleStore is null)
        {
            return true;
        }

        var schedule = await _scheduleStore.GetAsync(cancellationToken);
        var last = await _statusStore.GetAsync(OperationNames.DeviceTagging, cancellationToken);
        return last is null ||
            _timeProvider.GetUtcNow() >= last.StartedAtUtc + TimeSpan.FromMinutes(schedule.TaggingIntervalMinutes) - DueTolerance;
    }

    /// <summary>Runs device tagging requested through the management API.</summary>
    /// <param name="request">The queued run request.</param>
    /// <param name="cancellationToken">Cancels the invocation.</param>
    /// <returns>A task representing the asynchronous invocation.</returns>
    [Function("DeviceTaggingRequestFunction")]
    public async Task RunRequestedAsync(
        [QueueTrigger(OperationQueues.DeviceTagging)] OperationRequest request,
        CancellationToken cancellationToken)
        => await RunOperationAsync(request.InvocationId, false, true, cancellationToken);

    /// <summary>Runs device tagging when no other instance holds the operation lock.</summary>
    /// <remarks>
    /// The run is cancelled when either the host cancels the invocation or the blob lease backing the
    /// operation lock is lost, so a second instance can never tag devices concurrently. Requested runs
    /// that cannot start record a rejected status so the queued request does not remain pending.
    /// </remarks>
    /// <param name="invocationId">The identifier used to correlate logs, trace, and status.</param>
    /// <param name="isPastDue">Whether the timer invocation was late.</param>
    /// <param name="isRequested">
    /// <see langword="true"/> when the run was queued through the management API.
    /// </param>
    /// <param name="cancellationToken">Cancels the invocation.</param>
    /// <returns>The run result, or <see langword="null"/> when another run is active.</returns>
    internal async Task<TaggingResult?> RunOperationAsync(
        string invocationId,
        bool isPastDue,
        bool isRequested,
        CancellationToken cancellationToken)
    {
        IOperationLease? lease;
        try
        {
            lease = await _operationLock.TryAcquireAsync(OperationNames.DeviceTagging, cancellationToken);
        }
        catch (Exception exception) when (isRequested)
        {
            // The run never started, so RunCoreAsync cannot record the failure for this request.
            await TryRecordStatusAsync(() => _statusStore.RejectQueuedAsync(
                OperationNames.DeviceTagging,
                invocationId,
                exception,
                CancellationToken.None));
            throw;
        }

        await using var leaseScope = lease;
        if (lease is null)
        {
            _logger.LogWarning(
                "Device tagging skipped because another run is active. Invocation ID: {InvocationId}",
                invocationId);
            if (isRequested)
            {
                await TryRecordStatusAsync(() => _statusStore.RejectQueuedAsync(
                    OperationNames.DeviceTagging,
                    invocationId,
                    new OperationAlreadyRunningException(OperationNames.DeviceTagging),
                    CancellationToken.None));
            }

            return null;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            lease.LeaseLost);
        return await RunCoreAsync(invocationId, isPastDue, linkedCancellation.Token);
    }

    /// <summary>
    /// Executes the tagging workflow while holding the lease and records trace, status, and log output.
    /// </summary>
    /// <param name="invocationId">The identifier used to correlate logs, trace, and status.</param>
    /// <param name="isPastDue">Whether the timer invocation was late.</param>
    /// <param name="cancellationToken">Cancels the run, including when the lease is lost.</param>
    /// <returns>The aggregate tagging result.</returns>
    /// <exception cref="Exception">
    /// Any failure from the tagging workflow is recorded as a failed run and rethrown.
    /// </exception>
    private async Task<TaggingResult> RunCoreAsync(
        string invocationId,
        bool isPastDue,
        CancellationToken cancellationToken)
    {
        var trace = await _runTraceStore.StartAsync(
            invocationId,
            isPastDue,
            cancellationToken);
        await TryRecordStatusAsync(() => _statusStore.StartAsync(
            OperationNames.DeviceTagging,
            invocationId,
            cancellationToken));
        _logger.LogInformation(
            "Device tagging started. Invocation ID: {InvocationId}, Past due: {IsPastDue}",
            invocationId,
            isPastDue);

        try
        {
            var result = await _deviceTaggingService.TagDevicesAsync(cancellationToken, trace);

            await trace.WriteAsync(
                "Information",
                "Invocation",
                "Complete",
                "Succeeded",
                new Dictionary<string, object?>
                {
                    ["discovered"] = result.Discovered,
                    ["updated"] = result.Updated,
                    ["unchanged"] = result.Unchanged,
                    ["notFound"] = result.NotFound,
                    ["notHybridJoined"] = result.NotHybridJoined,
                    ["membershipsAdded"] = result.MembershipsAdded,
                    ["membershipsRemoved"] = result.MembershipsRemoved,
                    ["removedFromScope"] = result.RemovedFromScope
                },
                null,
                CancellationToken.None);
            await TryRecordStatusAsync(() => _statusStore.CompleteAsync(
                OperationNames.DeviceTagging,
                invocationId,
                new Dictionary<string, object?>
                {
                    ["discovered"] = result.Discovered,
                    ["updated"] = result.Updated,
                    ["unchanged"] = result.Unchanged,
                    ["notFound"] = result.NotFound,
                    ["notHybridJoined"] = result.NotHybridJoined,
                    ["membershipsAdded"] = result.MembershipsAdded,
                    ["membershipsRemoved"] = result.MembershipsRemoved,
                    ["removedFromScope"] = result.RemovedFromScope
                },
                false,
                CancellationToken.None));

            _logger.LogInformation(
                "Device tagging completed. Invocation ID: {InvocationId}, Discovered: {Discovered}, Updated: {Updated}, Already tagged: {Unchanged}, Not found: {NotFound}, Not hybrid joined: {NotHybridJoined}, Memberships added: {MembershipsAdded}, Memberships removed: {MembershipsRemoved}, Removed from scope: {RemovedFromScope}",
                invocationId,
                result.Discovered,
                result.Updated,
                result.Unchanged,
                result.NotFound,
                result.NotHybridJoined,
                result.MembershipsAdded,
                result.MembershipsRemoved,
                result.RemovedFromScope);
            _activityLog.Record(new ActivityEntry(
                ActivityCategories.Tagging,
                "TaggingRunCompleted",
                ActivityOutcomes.Succeeded,
                "System",
                $"Invocation {invocationId}",
                $"Discovered: {result.Discovered}, updated: {result.Updated}, unchanged: {result.Unchanged}, " +
                $"not found: {result.NotFound}, not hybrid joined: {result.NotHybridJoined}, " +
                $"memberships added: {result.MembershipsAdded}, removed: {result.MembershipsRemoved}, " +
                $"removed from scope: {result.RemovedFromScope}" + FormatChangedDevices(result.ChangedDeviceNames)));
            return result;
        }
        catch (Exception exception)
        {
            await trace.WriteAsync(
                "Error",
                "Invocation",
                "Complete",
                "Failed",
                null,
                exception,
                CancellationToken.None);
            await TryRecordStatusAsync(() => _statusStore.FailAsync(
                OperationNames.DeviceTagging,
                invocationId,
                exception,
                CancellationToken.None));
            _logger.LogError(
                exception,
                "Device tagging failed. Invocation ID: {InvocationId}",
                invocationId);
            _activityLog.Record(new ActivityEntry(
                ActivityCategories.Tagging,
                "TaggingRunCompleted",
                ActivityOutcomes.Failed,
                "System",
                $"Invocation {invocationId}",
                exception.Message));
            throw;
        }
    }

    /// <summary>Lists the changed Entra devices for the activity log, limited to 50 names.</summary>
    /// <param name="names">The changed device names.</param>
    /// <returns>The suffix, or an empty string when no device changed.</returns>
    private static string FormatChangedDevices(IReadOnlyList<string>? names)
    {
        if (names is null || names.Count == 0)
        {
            return string.Empty;
        }
        var listed = string.Join(", ", names.Take(50));
        return names.Count > 50
            ? $"; changed Entra devices: {listed} and {names.Count - 50} more"
            : $"; changed Entra devices: {listed}";
    }

    /// <summary>Writes run status without letting status-store failures fail the run itself.</summary>
    /// <param name="writeStatus">The status write to execute.</param>
    /// <returns>A task that completes after the write succeeded or the failure was logged.</returns>
    private async Task TryRecordStatusAsync(Func<Task> writeStatus)
    {
        try
        {
            await writeStatus();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to persist the device-tagging run status");
        }
    }
}