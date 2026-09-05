/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
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