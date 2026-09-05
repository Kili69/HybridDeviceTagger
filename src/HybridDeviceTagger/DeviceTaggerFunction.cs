/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using HybridDeviceTagger.Models;
using HybridDeviceTagger.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HybridDeviceTagger;

/// <summary>Runs the scheduled hybrid-device tagging workflow.</summary>
public class DeviceTaggerFunction : IDeviceTaggingRunner
{
    private static readonly SemaphoreSlim ExecutionLock = new(1, 1);
    private readonly DeviceTaggingService _deviceTaggingService;
    private readonly IRunTraceStore _runTraceStore;
    private readonly IOperationStatusStore _statusStore;
    private readonly ILogger<DeviceTaggerFunction> _logger;

    /// <summary>Initializes a new instance of the <see cref="DeviceTaggerFunction"/> class.</summary>
    /// <param name="deviceTaggingService">The device-tagging orchestrator.</param>
    /// <param name="runTraceStore">The latest-run diagnostic trace store.</param>
    /// <param name="statusStore">The latest-run status store.</param>
    /// <param name="logger">The structured logger.</param>
    public DeviceTaggerFunction(
        DeviceTaggingService deviceTaggingService,
        IRunTraceStore runTraceStore,
        IOperationStatusStore statusStore,
        ILogger<DeviceTaggerFunction> logger)
    {
        _deviceTaggingService = deviceTaggingService;
        _runTraceStore = runTraceStore;
        _statusStore = statusStore;
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
        => await RunOperationAsync(context.InvocationId, timer.IsPastDue, cancellationToken);

    /// <inheritdoc />
    Task<TaggingResult> IDeviceTaggingRunner.RunAsync(
        string invocationId,
        bool isPastDue,
        CancellationToken cancellationToken)
        => RunOperationAsync(invocationId, isPastDue, cancellationToken);

    private async Task<TaggingResult> RunOperationAsync(
        string invocationId,
        bool isPastDue,
        CancellationToken cancellationToken)
    {
        if (!await ExecutionLock.WaitAsync(0, cancellationToken))
        {
            throw new OperationAlreadyRunningException(OperationNames.DeviceTagging);
        }

        try
        {
            return await RunCoreAsync(invocationId, isPastDue, cancellationToken);
        }
        finally
        {
            ExecutionLock.Release();
        }
    }

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