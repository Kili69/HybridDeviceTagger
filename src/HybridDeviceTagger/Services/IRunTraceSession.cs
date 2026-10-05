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

/// <summary>Writes ordered, structured diagnostic events for one tagging invocation.</summary>
public interface IRunTraceSession
{
    /// <summary>Writes one action or decision to the current run trace.</summary>
    Task WriteAsync(
        string level,
        string phase,
        string action,
        string outcome,
        IReadOnlyDictionary<string, object?>? details,
        Exception? exception,
        CancellationToken cancellationToken);
}

/// <summary>Starts the diagnostic trace that represents the latest tagging invocation.</summary>
public interface IRunTraceStore
{
    /// <summary>Replaces the previous trace and starts a new invocation trace.</summary>
    Task<IRunTraceSession> StartAsync(
        string invocationId,
        bool isPastDue,
        CancellationToken cancellationToken);
}

/// <summary>Provides reusable helpers for structured run tracing.</summary>
public static class RunTrace
{
    /// <summary>Returns a trace session that discards all events.</summary>
    public static IRunTraceSession None { get; } = new NullRunTraceSession();

    private sealed class NullRunTraceSession : IRunTraceSession
    {
        public Task WriteAsync(
            string level,
            string phase,
            string action,
            string outcome,
            IReadOnlyDictionary<string, object?>? details,
            Exception? exception,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}