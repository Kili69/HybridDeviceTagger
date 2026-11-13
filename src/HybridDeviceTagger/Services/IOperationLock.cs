/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

namespace HybridDeviceTagger.Services;

/// <summary>Provides an exclusive lock per operation across all Function App instances.</summary>
/// <remarks>
/// Flex Consumption runs timer, queue, and HTTP functions in separate instance groups, so an
/// in-process lock cannot prevent parallel password rotations or tagging runs.
/// </remarks>
public interface IOperationLock
{
    /// <summary>Acquires the operation lock or returns <see langword="null"/> when it is held.</summary>
    /// <param name="operation">The operation name, for example <see cref="OperationNames.DeviceTagging"/>.</param>
    /// <param name="cancellationToken">Cancels the acquisition.</param>
    /// <returns>The held lease, which the caller must dispose; or <see langword="null"/>.</returns>
    Task<IOperationLease?> TryAcquireAsync(string operation, CancellationToken cancellationToken);

    /// <summary>Returns whether another run currently holds the operation lock.</summary>
    /// <remarks>The result is a point-in-time observation and does not reserve the lock.</remarks>
    /// <param name="operation">The operation name.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns><see langword="true"/> when the lock is currently held.</returns>
    Task<bool> IsHeldAsync(string operation, CancellationToken cancellationToken);
}

/// <summary>Represents a held operation lock that is released on disposal.</summary>
public interface IOperationLease : IAsyncDisposable
{
    /// <summary>Gets a token that is canceled when the lock can no longer be renewed.</summary>
    CancellationToken LeaseLost { get; }
}

/// <summary>Indicates that the requested operation is already executing.</summary>
/// <param name="operation">The name of the operation that is already running.</param>
public sealed class OperationAlreadyRunningException(string operation)
    : InvalidOperationException($"The {operation} operation is already running.");
