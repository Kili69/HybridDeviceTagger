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

/// <summary>Runs device tagging through the same orchestration path for timer and management requests.</summary>
public interface IDeviceTaggingRunner
{
    /// <summary>Runs one device-tagging operation.</summary>
    Task<TaggingResult> RunAsync(
        string invocationId,
        bool isPastDue,
        CancellationToken cancellationToken);
}

/// <summary>Runs password rotation through the same orchestration path for timer and management requests.</summary>
public interface IPasswordRotationRunner
{
    /// <summary>Runs one password-rotation operation.</summary>
    Task<PasswordRotationResult> RunAsync(
        string invocationId,
        bool isPastDue,
        bool force,
        CancellationToken cancellationToken);
}

/// <summary>Indicates that the requested management operation is already executing.</summary>
public sealed class OperationAlreadyRunningException(string operation)
    : InvalidOperationException($"The {operation} operation is already running.");