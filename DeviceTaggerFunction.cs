using HybridDeviceTagger.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HybridDeviceTagger;

public class DeviceTaggerFunction
{
    private readonly DeviceTaggingService _deviceTaggingService;
    private readonly ILogger<DeviceTaggerFunction> _logger;

    public DeviceTaggerFunction(
        DeviceTaggingService deviceTaggingService,
        ILogger<DeviceTaggerFunction> logger)
    {
        _deviceTaggingService = deviceTaggingService;
        _logger = logger;
    }

    [Function("DeviceTaggerFunction")]
    public async Task RunAsync(
        [TimerTrigger("%TaggingSchedule%")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var result = await _deviceTaggingService.TagDevicesAsync(cancellationToken);

        _logger.LogInformation(
            "Device tagging completed. Discovered: {Discovered}, Updated: {Updated}, Already tagged: {Unchanged}, Not found: {NotFound}, Not hybrid joined: {NotHybridJoined}",
            result.Discovered,
            result.Updated,
            result.Unchanged,
            result.NotFound,
            result.NotHybridJoined);
    }
}