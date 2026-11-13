/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

namespace HybridDeviceTagger.Services;

/// <summary>Represents a streamed latest-run trace download.</summary>
/// <param name="Content">The trace content stream, owned and disposed by the HTTP response.</param>
/// <param name="ContentType">The MIME content type of the trace.</param>
/// <param name="FileName">The suggested download file name.</param>
public sealed record RunTraceDownload(
    Stream Content,
    string ContentType,
    string FileName);

/// <summary>Reads the latest device-tagging diagnostic trace.</summary>
public interface IRunTraceReader
{
    /// <summary>Opens the latest trace, or returns <see langword="null"/> before the first run.</summary>
    /// <param name="cancellationToken">Cancels opening the trace.</param>
    /// <returns>The streamed trace, or <see langword="null"/> when none exists.</returns>
    Task<RunTraceDownload?> OpenLatestAsync(CancellationToken cancellationToken);
}