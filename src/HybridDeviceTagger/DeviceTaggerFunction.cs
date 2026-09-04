using HybridDeviceTagger.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HybridDeviceTagger;

/// <summary>Runs the scheduled hybrid-device tagging workflow.</summary>
public class DeviceTaggerFunction
{
    private readonly DeviceTaggingService _deviceTaggingService;
    private readonly ILogger<DeviceTaggerFunction> _logger;

    /// <summary>Initializes a new instance of the <see cref="DeviceTaggerFunction"/> class.</summary>
    /// <param name="deviceTaggingService">The device-tagging orchestrator.</param>
    /// <param name="logger">The structured logger.</param>
    public DeviceTaggerFunction(
        DeviceTaggingService deviceTaggingService,
        ILogger<DeviceTaggerFunction> logger)
    {
        _deviceTaggingService = deviceTaggingService;
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
    {
        _logger.LogInformation(
            "Device tagging started. Invocation ID: {InvocationId}, Past due: {IsPastDue}",
            context.InvocationId,
            timer.IsPastDue);

        try
        {
            var result = await _deviceTaggingService.TagDevicesAsync(cancellationToken);

            _logger.LogInformation(
                "Device tagging completed. Invocation ID: {InvocationId}, Discovered: {Discovered}, Updated: {Updated}, Already tagged: {Unchanged}, Not found: {NotFound}, Not hybrid joined: {NotHybridJoined}, Memberships added: {MembershipsAdded}, Memberships removed: {MembershipsRemoved}",
                context.InvocationId,
                result.Discovered,
                result.Updated,
                result.Unchanged,
                result.NotFound,
                result.NotHybridJoined,
                result.MembershipsAdded,
                result.MembershipsRemoved);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Device tagging failed. Invocation ID: {InvocationId}",
                context.InvocationId);
            throw;
        }
    }
}