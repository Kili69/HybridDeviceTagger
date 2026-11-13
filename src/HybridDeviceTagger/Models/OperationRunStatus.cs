/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using System.Text.Json.Serialization;

namespace HybridDeviceTagger.Models;

/// <summary>Identifies the lifecycle state of one scheduled operation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OperationRunState>))]
public enum OperationRunState
{
    /// <summary>The operation was requested and waits for a worker.</summary>
    Queued,
    /// <summary>The operation is currently running.</summary>
    Running,
    /// <summary>The operation completed successfully.</summary>
    Succeeded,
    /// <summary>The operation failed.</summary>
    Failed
}

/// <summary>Describes the latest persisted execution of one scheduled operation.</summary>
/// <param name="Operation">The operation name, for example <c>device-tagging</c>.</param>
/// <param name="InvocationId">The invocation identifier that correlates status, logs, and traces.</param>
/// <param name="State">The lifecycle state of the run.</param>
/// <param name="StartedAtUtc">The UTC time at which the run was queued or started.</param>
/// <param name="CompletedAtUtc">The UTC time at which the run finished, if it has finished.</param>
/// <param name="LastPasswordChangeAtUtc">
/// The UTC time of the last actual Active Directory password change; only set for password rotation.
/// </param>
/// <param name="Result">Aggregate, non-sensitive result values of a successful run.</param>
/// <param name="ErrorType">The exception type name of a failed run.</param>
/// <param name="ErrorMessage">The exception message of a failed run.</param>
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