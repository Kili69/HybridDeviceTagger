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
    /// <remarks>Implementations must not throw for storage failures so tracing never fails a run.</remarks>
    /// <param name="level">The severity, for example <c>Information</c>, <c>Warning</c>, or <c>Error</c>.</param>
    /// <param name="phase">The workflow phase, for example <c>Discovery</c> or <c>Device</c>.</param>
    /// <param name="action">The action within the phase.</param>
    /// <param name="outcome">The action outcome, for example <c>Started</c> or <c>Succeeded</c>.</param>
    /// <param name="details">Optional structured details; must not contain secrets.</param>
    /// <param name="exception">The optional exception associated with a failure.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task representing the asynchronous write.</returns>
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
    /// <param name="invocationId">The invocation identifier written into every event.</param>
    /// <param name="isPastDue">Whether the timer invocation was late.</param>
    /// <param name="cancellationToken">Cancels initialization.</param>
    /// <returns>The trace session, or a no-op session when the trace cannot be initialized.</returns>
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

    /// <summary>A trace session that ignores all events; used when tracing is unavailable or not needed.</summary>
    private sealed class NullRunTraceSession : IRunTraceSession
    {
        /// <inheritdoc />
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