/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace HybridDeviceTagger.Services;

/// <summary>Validates the principal header emitted by App Service Easy Auth.</summary>
public static class EasyAuthPrincipalValidator
{
    private const string PrincipalHeader = "X-MS-CLIENT-PRINCIPAL";

    /// <summary>Returns whether the request contains a structurally valid Entra principal.</summary>
    public static bool IsAuthenticated(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(PrincipalHeader, out var values) ||
            values.Count != 1)
        {
            return false;
        }

        try
        {
            var payload = Convert.FromBase64String(values[0]!);
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            return root.TryGetProperty("auth_typ", out var authenticationType) &&
                string.Equals(authenticationType.GetString(), "aad", StringComparison.OrdinalIgnoreCase) &&
                root.TryGetProperty("claims", out var claims) &&
                claims.ValueKind == JsonValueKind.Array &&
                claims.GetArrayLength() > 0;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}