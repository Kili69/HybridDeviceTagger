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
using HybridDeviceTagger.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HybridDeviceTagger;

/// <summary>Runs the scheduled and requested Active Directory password-rotation workflow.</summary>
public sealed class PasswordRotationFunction
{
    /// <summary>The orchestrator that rotates or recovers the directory service credential.</summary>
    private readonly PasswordRotationService _rotationService;

    /// <summary>Persists the latest-run status shown by the management API.</summary>
    private readonly IOperationStatusStore _statusStore;

    /// <summary>Prevents concurrent password-rotation runs across Function instances.</summary>
    private readonly IOperationLock _operationLock;

    /// <summary>The structured logger for invocation-level events.</summary>
    private readonly ILogger<PasswordRotationFunction> _logger;

    /// <summary>Initializes a new instance of the <see cref="PasswordRotationFunction"/> class.</summary>
    /// <param name="rotationService">The password-rotation orchestrator.</param>
    /// <param name="statusStore">The latest-run status store.</param>
    /// <param name="operationLock">The cross-instance operation lock.</param>
    /// <param name="logger">The structured logger.</param>
    public PasswordRotationFunction(
        PasswordRotationService rotationService,
        IOperationStatusStore statusStore,
        IOperationLock operationLock,
        ILogger<PasswordRotationFunction> logger)
    {
        _rotationService = rotationService;
        _statusStore = statusStore;
        _operationLock = operationLock;
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

    /// <summary>Forces a password rotation requested through the management API.</summary>
    /// <param name="request">The queued run request.</param>
    /// <param name="cancellationToken">Cancels the invocation.</param>
    /// <returns>A task representing the asynchronous invocation.</returns>
    [Function("PasswordRotationRequestFunction")]
    public async Task RunRequestedAsync(
        [QueueTrigger(OperationQueues.PasswordRotation)] OperationRequest request,
        CancellationToken cancellationToken)
        => await RunOperationAsync(request.InvocationId, false, true, cancellationToken);

    /// <summary>Runs password rotation when no other instance holds the operation lock.</summary>
    /// <remarks>
    /// The lock prevents a second run from overwriting the staged secret while the first run is
    /// changing the Active Directory password, which would make recovery impossible.
    /// </remarks>
    /// <param name="invocationId">The identifier used to correlate logs and status.</param>
    /// <param name="isPastDue">Whether the timer invocation was late.</param>
    /// <param name="force">
    /// <see langword="true"/> to rotate regardless of password age; used for API-requested runs.
    /// </param>
    /// <param name="cancellationToken">Cancels the invocation.</param>
    /// <returns>The rotation result, or <see langword="null"/> when another run is active.</returns>
    internal async Task<PasswordRotationResult?> RunOperationAsync(
        string invocationId,
        bool isPastDue,
        bool force,
        CancellationToken cancellationToken)
    {
        IOperationLease? lease;
        try
        {
            lease = await _operationLock.TryAcquireAsync(OperationNames.PasswordRotation, cancellationToken);
        }
        catch (Exception exception) when (force)
        {
            // The run never started, so RunCoreAsync cannot record the failure for this request.
            await TryRecordStatusAsync(() => _statusStore.RejectQueuedAsync(
                OperationNames.PasswordRotation,
                invocationId,
                exception,
                CancellationToken.None));
            throw;
        }

        await using var leaseScope = lease;
        if (lease is null)
        {
            _logger.LogWarning(
                "Password rotation skipped because another run is active. Invocation ID: {InvocationId}",
                invocationId);
            if (force)
            {
                await TryRecordStatusAsync(() => _statusStore.RejectQueuedAsync(
                    OperationNames.PasswordRotation,
                    invocationId,
                    new OperationAlreadyRunningException(OperationNames.PasswordRotation),
                    CancellationToken.None));
            }

            return null;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            lease.LeaseLost);
        return await RunCoreAsync(invocationId, isPastDue, force, linkedCancellation.Token);
    }

    /// <summary>Executes the rotation while holding the lease and records status and log output.</summary>
    /// <param name="invocationId">The identifier used to correlate logs and status.</param>
    /// <param name="isPastDue">Whether the timer invocation was late.</param>
    /// <param name="force"><see langword="true"/> to rotate regardless of password age.</param>
    /// <param name="cancellationToken">Cancels the run, including when the lease is lost.</param>
    /// <returns>The rotation result.</returns>
    /// <exception cref="Exception">
    /// Any failure from the rotation workflow is recorded as a failed run and rethrown.
    /// </exception>
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
            _logger.LogWarning(exception, "Unable to persist the password-rotation run status");
        }
    }
}