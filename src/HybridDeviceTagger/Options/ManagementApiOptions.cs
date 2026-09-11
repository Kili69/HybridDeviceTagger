/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using System.ComponentModel.DataAnnotations;

namespace HybridDeviceTagger.Options;

/// <summary>Configures public authentication metadata for the management API.</summary>
public sealed class ManagementApiOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "ManagementApi";

    /// <summary>Gets or sets the Entra application client ID protecting the API.</summary>
    [Required]
    [RegularExpression("^[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$")]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Gets the delegated token scope consumed by REST clients.</summary>
    public string TokenScope => $"api://{ClientId}/.default";
}