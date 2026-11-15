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

namespace HybridDeviceTagger.Services;

/// <summary>Defines the Storage queues that carry management-API run requests.</summary>
public static class OperationQueues
{
    /// <summary>Queue processed by the device-tagging worker.</summary>
    public const string DeviceTagging = "device-tagging-requests";
    /// <summary>Queue processed by the forced password-rotation worker.</summary>
    public const string PasswordRotation = "password-rotation-requests";

    /// <summary>Gets the queue name for an operation.</summary>
    /// <param name="operation">The operation name.</param>
    /// <returns>The Storage queue name.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The operation name is not supported.</exception>
    public static string For(string operation) => operation switch
    {
        OperationNames.DeviceTagging => DeviceTagging,
        OperationNames.PasswordRotation => PasswordRotation,
        _ => throw new ArgumentOutOfRangeException(
            nameof(operation),
            operation,
            "Unsupported operation name.")
    };
}

/// <summary>Submits management-API run requests for asynchronous execution.</summary>
public interface IOperationRequestQueue
{
    /// <summary>Enqueues one run request.</summary>
    /// <param name="operation">The operation name that selects the queue.</param>
    /// <param name="request">The request to serialize into the message.</param>
    /// <param name="cancellationToken">Cancels the enqueue operation.</param>
    /// <returns>A task representing the asynchronous enqueue operation.</returns>
    Task EnqueueAsync(
        string operation,
        OperationRequest request,
        CancellationToken cancellationToken);
}
