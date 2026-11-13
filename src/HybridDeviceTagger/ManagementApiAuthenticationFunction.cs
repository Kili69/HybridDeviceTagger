/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using HybridDeviceTagger.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger;

/// <summary>Publishes the token scope needed to authenticate management REST API calls.</summary>
public sealed class ManagementApiAuthenticationFunction
{
    /// <summary>The validated management API options containing the published token scope.</summary>
    private readonly ManagementApiOptions _options;

    /// <summary>Initializes the authentication metadata endpoint.</summary>
    /// <param name="options">The management API options.</param>
    public ManagementApiAuthenticationFunction(IOptions<ManagementApiOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Gets the public delegated token scope for this deployment.</summary>
    /// <remarks>
    /// The endpoint is anonymous so clients can discover which scope to request before they hold a
    /// token. It returns only the non-secret scope string.
    /// </remarks>
    /// <param name="request">The incoming HTTP request.</param>
    /// <returns>An HTTP 200 result with a <c>tokenScope</c> property.</returns>
    [Function("GetManagementApiAuthentication")]
    public IActionResult GetAuthentication(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "management/authentication")]
        HttpRequest request) =>
        new OkObjectResult(new { tokenScope = _options.TokenScope });
}