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
