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

using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace HybridDeviceTagger.Services;

/// <summary>Defines Microsoft Entra application roles accepted by the management API.</summary>
public static class ManagementApiRoles
{
    /// <summary>Reads rules, statuses, and traces.</summary>
    public const string Reader = "HybridDeviceTagger.Reader";
    /// <summary>Reads and changes tagging rules.</summary>
    public const string RuleAdministrator = "HybridDeviceTagger.RuleAdministrator";
    /// <summary>Reads tagging diagnostics and starts tagging runs.</summary>
    public const string TaggingOperator = "HybridDeviceTagger.TaggingOperator";
    /// <summary>Reads rotation status and forces password rotation.</summary>
    public const string PasswordRotationAdministrator = "HybridDeviceTagger.PasswordRotationAdministrator";
    /// <summary>Reads and changes Active Directory LDAPS trust certificates.</summary>
    public const string CertificateAdministrator = "HybridDeviceTagger.CertificateAdministrator";
}

/// <summary>Describes the result of management API principal authorization.</summary>
public enum EasyAuthAuthorizationResult
{
    /// <summary>No structurally valid Microsoft Entra principal was supplied.</summary>
    Unauthenticated,
    /// <summary>The principal is authenticated but lacks every required role.</summary>
    Forbidden,
    /// <summary>The principal has at least one required role.</summary>
    Authorized
}

/// <summary>Validates the principal header emitted by App Service Easy Auth.</summary>
public static class EasyAuthPrincipalValidator
{
    private const string PrincipalHeader = "X-MS-CLIENT-PRINCIPAL";
    private const string ObjectIdClaim = "oid";
    private const string ObjectIdClaimUri =
        "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string RoleClaim = "roles";
    private const string RoleClaimUri = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

    /// <summary>Returns whether the request contains a structurally valid Entra principal.</summary>
    public static bool IsAuthenticated(HttpRequest request)
        => ReadClaims(request) is not null;

    /// <summary>Gets the authenticated principal's Microsoft Entra object ID.</summary>
    public static bool TryGetObjectId(HttpRequest request, out Guid objectId)
    {
        var claim = ReadClaims(request)?.FirstOrDefault(claim =>
            string.Equals(claim.Type, ObjectIdClaim, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(claim.Type, ObjectIdClaimUri, StringComparison.OrdinalIgnoreCase));
        return Guid.TryParse(claim?.Value, out objectId);
    }

    /// <summary>Returns whether an Entra principal has at least one required application role.</summary>
    public static EasyAuthAuthorizationResult Authorize(
        HttpRequest request,
        params string[] requiredRoles)
    {
        var claims = ReadClaims(request);
        if (claims is null)
        {
            return EasyAuthAuthorizationResult.Unauthenticated;
        }

        return claims.Any(claim =>
                (string.Equals(claim.Type, RoleClaim, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(claim.Type, RoleClaimUri, StringComparison.OrdinalIgnoreCase)) &&
                requiredRoles.Contains(claim.Value, StringComparer.Ordinal))
            ? EasyAuthAuthorizationResult.Authorized
            : EasyAuthAuthorizationResult.Forbidden;
    }

    private static IReadOnlyList<(string Type, string Value)>? ReadClaims(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(PrincipalHeader, out var values) || values.Count != 1)
        {
            return null;
        }

        try
        {
            var payload = Convert.FromBase64String(values[0]!);
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (!root.TryGetProperty("auth_typ", out var authenticationType) ||
                !string.Equals(authenticationType.GetString(), "aad", StringComparison.OrdinalIgnoreCase) ||
                !root.TryGetProperty("claims", out var claims) ||
                claims.ValueKind != JsonValueKind.Array ||
                claims.GetArrayLength() == 0)
            {
                return null;
            }

            var parsedClaims = new List<(string Type, string Value)>();
            foreach (var claim in claims.EnumerateArray())
            {
                if (claim.TryGetProperty("typ", out var type) &&
                    claim.TryGetProperty("val", out var value) &&
                    type.ValueKind == JsonValueKind.String &&
                    value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(type.GetString()) &&
                    !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    parsedClaims.Add((type.GetString()!, value.GetString()!));
                }
            }

            return parsedClaims.Count > 0 ? parsedClaims : null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}