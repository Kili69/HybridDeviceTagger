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

using System.Text.Json.Serialization;

namespace HybridDeviceTagger.Models;

/// <summary>Identifies the lifecycle state of one scheduled operation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OperationRunState>))]
public enum OperationRunState
{
    /// <summary>The operation is currently running.</summary>
    Running,
    /// <summary>The operation completed successfully.</summary>
    Succeeded,
    /// <summary>The operation failed.</summary>
    Failed
}

/// <summary>Describes the latest persisted execution of one scheduled operation.</summary>
public sealed record OperationRunStatus(
    string Operation,
    string InvocationId,
    OperationRunState State,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? LastPasswordChangeAtUtc,
    IReadOnlyDictionary<string, object?>? Result,
    string? ErrorType,
    string? ErrorMessage);