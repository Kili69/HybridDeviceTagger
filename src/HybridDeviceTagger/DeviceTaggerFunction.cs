/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using HybridDeviceTagger.Models;
using HybridDeviceTagger.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

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

    /// <summary>Initializes a new instance of the <see cref="DeviceTaggerFunction"/> class.</summary>
    /// <param name="deviceTaggingService">The device-tagging orchestrator.</param>
    /// <param name="runTraceStore">The latest-run diagnostic trace store.</param>
    /// <param name="statusStore">The latest-run status store.</param>
    /// <param name="operationLock">The cross-instance operation lock.</param>
    /// <param name="logger">The structured logger.</param>
    public DeviceTaggerFunction(
        DeviceTaggingService deviceTaggingService,
        IRunTraceStore runTraceStore,
        IOperationStatusStore statusStore,
        IOperationLock operationLock,
        ILogger<DeviceTaggerFunction> logger)
    {
        _deviceTaggingService = deviceTaggingService;
        _runTraceStore = runTraceStore;
        _statusStore = statusStore;
        _operationLock = operationLock;
        _logger = logger;
    }

    /// <summary>Tags eligible devices and logs aggregate counts without directory credentials.</summary>
    /// <param name="timer">Information about the timer invocation.</param>
    /// <param name="context">The Functions invocation context used for log correlation.</param>
    /// <param name="cancellationToken">Cancels the invocation.</param>
    /// <returns>A task representing the asynchronous invocation.</returns>
    [Function("DeviceTaggerFunction")]
    public async Task RunAsync(
        [TimerTrigger("%TaggingSchedule%")] TimerInfo timer,
        FunctionContext context,
        CancellationToken cancellationToken)
        => await RunOperationAsync(context.InvocationId, timer.IsPastDue, false, cancellationToken);

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
            throw;
        }
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