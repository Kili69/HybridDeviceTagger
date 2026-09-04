using HybridDeviceTagger.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HybridDeviceTagger;

/// <summary>Runs the scheduled Active Directory password-rotation check.</summary>
public sealed class PasswordRotationFunction
{
    private readonly PasswordRotationService _rotationService;
    private readonly ILogger<PasswordRotationFunction> _logger;

    /// <summary>Initializes a new instance of the <see cref="PasswordRotationFunction"/> class.</summary>
    /// <param name="rotationService">The password-rotation orchestrator.</param>
    /// <param name="logger">The structured logger.</param>
    public PasswordRotationFunction(
        PasswordRotationService rotationService,
        ILogger<PasswordRotationFunction> logger)
    {
        _rotationService = rotationService;
        _logger = logger;
    }

    /// <summary>Rotates or recovers the service-account credential when required.</summary>
    /// <param name="timer">Information about the timer invocation.</param>
    /// <param name="context">The Functions invocation context used for log correlation.</param>
    /// <param name="cancellationToken">Cancels the invocation.</param>
    /// <returns>A task representing the asynchronous invocation.</returns>
    [Function("PasswordRotationFunction")]
    public async Task RunAsync(
        [TimerTrigger("%PasswordRotationSchedule%")] TimerInfo timer,
        FunctionContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Password rotation check started. Invocation ID: {InvocationId}, Past due: {IsPastDue}",
            context.InvocationId,
            timer.IsPastDue);

        try
        {
            var result = await _rotationService.RotateIfDueAsync(cancellationToken);
            _logger.LogInformation(
                "Password rotation check completed. Invocation ID: {InvocationId}, Outcome: {Outcome}, Current age: {AgeDays} days",
                context.InvocationId,
                result.Outcome,
                result.CurrentPasswordAgeDays);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Password rotation check failed. Invocation ID: {InvocationId}",
                context.InvocationId);
            throw;
        }
    }
}