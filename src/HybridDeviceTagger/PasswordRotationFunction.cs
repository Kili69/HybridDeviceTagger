/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using HybridDeviceTagger.Models;
using HybridDeviceTagger.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HybridDeviceTagger;

/// <summary>Runs the scheduled Active Directory password-rotation check.</summary>
public sealed class PasswordRotationFunction : IPasswordRotationRunner
{
    private static readonly SemaphoreSlim ExecutionLock = new(1, 1);
    private readonly PasswordRotationService _rotationService;
    private readonly IOperationStatusStore _statusStore;
    private readonly ILogger<PasswordRotationFunction> _logger;

    /// <summary>Initializes a new instance of the <see cref="PasswordRotationFunction"/> class.</summary>
    /// <param name="rotationService">The password-rotation orchestrator.</param>
    /// <param name="statusStore">The latest-run status store.</param>
    /// <param name="logger">The structured logger.</param>
    public PasswordRotationFunction(
        PasswordRotationService rotationService,
        IOperationStatusStore statusStore,
        ILogger<PasswordRotationFunction> logger)
    {
        _rotationService = rotationService;
        _statusStore = statusStore;
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
        => await RunOperationAsync(context.InvocationId, timer.IsPastDue, false, cancellationToken);

    /// <inheritdoc />
    Task<PasswordRotationResult> IPasswordRotationRunner.RunAsync(
        string invocationId,
        bool isPastDue,
        bool force,
        CancellationToken cancellationToken)
        => RunOperationAsync(invocationId, isPastDue, force, cancellationToken);

    private async Task<PasswordRotationResult> RunOperationAsync(
        string invocationId,
        bool isPastDue,
        bool force,
        CancellationToken cancellationToken)
    {
        if (!await ExecutionLock.WaitAsync(0, cancellationToken))
        {
            throw new OperationAlreadyRunningException(OperationNames.PasswordRotation);
        }

        try
        {
            return await RunCoreAsync(invocationId, isPastDue, force, cancellationToken);
        }
        finally
        {
            ExecutionLock.Release();
        }
    }

    private async Task<PasswordRotationResult> RunCoreAsync(
        string invocationId,
        bool isPastDue,
        bool force,
        CancellationToken cancellationToken)
    {
        await TryRecordStatusAsync(() => _statusStore.StartAsync(
            OperationNames.PasswordRotation,
            invocationId,
            cancellationToken));
        _logger.LogInformation(
            "Password rotation check started. Invocation ID: {InvocationId}, Past due: {IsPastDue}",
            invocationId,
            isPastDue);

        try
        {
            var result = force
                ? await _rotationService.RotateNowAsync(cancellationToken)
                : await _rotationService.RotateIfDueAsync(cancellationToken);
            await TryRecordStatusAsync(() => _statusStore.CompleteAsync(
                OperationNames.PasswordRotation,
                invocationId,
                new Dictionary<string, object?>
                {
                    ["outcome"] = result.Outcome.ToString(),
                    ["currentPasswordAgeDays"] = result.CurrentPasswordAgeDays
                },
                result.Outcome is PasswordRotationOutcome.Rotated or
                    PasswordRotationOutcome.RecoveredFromNext,
                CancellationToken.None));
            _logger.LogInformation(
                "Password rotation check completed. Invocation ID: {InvocationId}, Outcome: {Outcome}, Current age: {AgeDays} days",
                invocationId,
                result.Outcome,
                result.CurrentPasswordAgeDays);
            return result;
        }
        catch (Exception exception)
        {
            await TryRecordStatusAsync(() => _statusStore.FailAsync(
                OperationNames.PasswordRotation,
                invocationId,
                exception,
                CancellationToken.None));
            _logger.LogError(
                exception,
                "Password rotation check failed. Invocation ID: {InvocationId}",
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
            _logger.LogWarning(exception, "Unable to persist the password-rotation run status");
        }
    }
}