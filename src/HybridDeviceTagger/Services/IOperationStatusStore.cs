/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
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