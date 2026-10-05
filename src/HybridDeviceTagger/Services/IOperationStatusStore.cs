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
    /// <summary>Marks an operation as running.</summary>
    Task StartAsync(string operation, string invocationId, CancellationToken cancellationToken);

    /// <summary>Marks an operation as successfully completed.</summary>
    Task CompleteAsync(
        string operation,
        string invocationId,
        IReadOnlyDictionary<string, object?> result,
        bool passwordChanged,
        CancellationToken cancellationToken);

    /// <summary>Marks an operation as failed without persisting a stack trace.</summary>
    Task FailAsync(
        string operation,
        string invocationId,
        Exception exception,
        CancellationToken cancellationToken);

    /// <summary>Gets the latest status, or <see langword="null"/> before the first run.</summary>
    Task<OperationRunStatus?> GetAsync(string operation, CancellationToken cancellationToken);
}