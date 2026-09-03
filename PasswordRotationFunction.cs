using HybridDeviceTagger.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HybridDeviceTagger;

public sealed class PasswordRotationFunction
{
    private readonly PasswordRotationService _rotationService;
    private readonly ILogger<PasswordRotationFunction> _logger;

    public PasswordRotationFunction(
        PasswordRotationService rotationService,
        ILogger<PasswordRotationFunction> logger)
    {
        _rotationService = rotationService;
        _logger = logger;
    }

    [Function("PasswordRotationFunction")]
    public async Task RunAsync(
        [TimerTrigger("%PasswordRotationSchedule%")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var result = await _rotationService.RotateIfDueAsync(cancellationToken);
        _logger.LogInformation(
            "Password rotation check completed with outcome {Outcome}; current age was {AgeDays} days.",
            result.Outcome,
            result.CurrentPasswordAgeDays);
    }
}