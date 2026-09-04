/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
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