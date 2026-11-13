/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using HybridDeviceTagger.Models;

namespace HybridDeviceTagger.Services;

/// <summary>Contains stable operation names exposed by the management API.</summary>
public static class OperationNames
{
    /// <summary>The hybrid-device tagging workflow.</summary>
    public const string DeviceTagging = "device-tagging";
    /// <summary>The Active Directory password-rotation workflow.</summary>
    public const string PasswordRotation = "password-rotation";
}

/// <summary>Persists and retrieves latest-run operation status.</summary>
public interface IOperationStatusStore
{
    /// <summary>Marks a requested operation as accepted and waiting for a worker.</summary>
    /// <remarks>The last actual password-change time of the previous status is preserved.</remarks>
    /// <param name="operation">The operation name from <see cref="OperationNames"/>.</param>
    /// <param name="invocationId">The invocation identifier of the run.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task representing the asynchronous write.</returns>
    Task QueueAsync(string operation, string invocationId, CancellationToken cancellationToken);

    /// <summary>Marks a still-queued request as failed before it could start.</summary>
    /// <remarks>A newer status from another invocation is never overwritten.</remarks>
    /// <param name="operation">The operation name from <see cref="OperationNames"/>.</param>
    /// <param name="invocationId">The invocation identifier of the run.</param>
    /// <param name="reason">The exception describing why the request could not start.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task representing the asynchronous write.</returns>
    Task RejectQueuedAsync(
        string operation,
        string invocationId,
        Exception reason,
        CancellationToken cancellationToken);

    /// <summary>Marks an operation as running.</summary>
    /// <remarks>The last actual password-change time of the previous status is preserved.</remarks>
    /// <param name="operation">The operation name from <see cref="OperationNames"/>.</param>
    /// <param name="invocationId">The invocation identifier of the run.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task representing the asynchronous write.</returns>
    Task StartAsync(string operation, string invocationId, CancellationToken cancellationToken);

    /// <summary>Marks an operation as successfully completed.</summary>
    /// <param name="operation">The operation name from <see cref="OperationNames"/>.</param>
    /// <param name="invocationId">The invocation identifier of the run.</param>
    /// <param name="result">Aggregate, non-sensitive result values to publish.</param>
    /// <param name="passwordChanged">
    /// <see langword="true"/> to record the completion time as the last actual password change.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task representing the asynchronous write.</returns>
    Task CompleteAsync(
        string operation,
        string invocationId,
        IReadOnlyDictionary<string, object?> result,
        bool passwordChanged,
        CancellationToken cancellationToken);

    /// <summary>Marks an operation as failed without persisting a stack trace.</summary>
    /// <param name="operation">The operation name from <see cref="OperationNames"/>.</param>
    /// <param name="invocationId">The invocation identifier of the run.</param>
    /// <param name="exception">The failure; only its type name and message are stored.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task representing the asynchronous write.</returns>
    Task FailAsync(
        string operation,
        string invocationId,
        Exception exception,
        CancellationToken cancellationToken);

    /// <summary>Gets the latest status, or <see langword="null"/> before the first run.</summary>
    /// <param name="operation">The operation name from <see cref="OperationNames"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The latest status, or <see langword="null"/>.</returns>
    Task<OperationRunStatus?> GetAsync(string operation, CancellationToken cancellationToken);
}