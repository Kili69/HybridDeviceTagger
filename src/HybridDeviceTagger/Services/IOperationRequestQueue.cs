/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
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
