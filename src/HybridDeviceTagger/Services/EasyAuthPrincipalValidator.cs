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
/// <remarks>
/// App Service strips client-supplied <c>X-MS-CLIENT-PRINCIPAL</c> headers and injects its own after
/// validating the token, so the header is trusted only when Easy Auth is enabled in front of the app.
/// Only principals authenticated by Microsoft Entra (<c>auth_typ</c> <c>aad</c>) are accepted.
/// </remarks>
public static class EasyAuthPrincipalValidator
{
    /// <summary>The header containing the Base64-encoded Easy Auth principal JSON.</summary>
    private const string PrincipalHeader = "X-MS-CLIENT-PRINCIPAL";

    /// <summary>The short Entra object ID claim type.</summary>
    private const string ObjectIdClaim = "oid";

    /// <summary>The URI form of the Entra object ID claim type.</summary>
    private const string ObjectIdClaimUri =
        "http://schemas.microsoft.com/identity/claims/objectidentifier";

    /// <summary>The short application-role claim type.</summary>
    private const string RoleClaim = "roles";

    /// <summary>The URI form of the role claim type.</summary>
    private const string RoleClaimUri = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

    /// <summary>Returns whether the request contains a structurally valid Entra principal.</summary>
    /// <param name="request">The HTTP request carrying the Easy Auth principal header.</param>
    /// <returns><see langword="true"/> when the principal header is valid and contains claims.</returns>
    public static bool IsAuthenticated(HttpRequest request)
        => ReadClaims(request) is not null;

    /// <summary>Gets the authenticated principal's Microsoft Entra object ID.</summary>
    /// <param name="request">The HTTP request carrying the Easy Auth principal header.</param>
    /// <param name="objectId">The object ID, or <see cref="Guid.Empty"/> when unavailable.</param>
    /// <returns><see langword="true"/> when a valid principal contains a GUID object ID claim.</returns>
    public static bool TryGetObjectId(HttpRequest request, out Guid objectId)
    {
        var claim = ReadClaims(request)?.FirstOrDefault(claim =>
            string.Equals(claim.Type, ObjectIdClaim, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(claim.Type, ObjectIdClaimUri, StringComparison.OrdinalIgnoreCase));
        return Guid.TryParse(claim?.Value, out objectId);
    }

    /// <summary>Gets a human-readable, non-secret description of the authenticated principal.</summary>
    /// <remarks>
    /// Combines the user principal name or display name with the object ID, for example
    /// <c>admin@contoso.com (oid 1111…)</c>. Returns <c>anonymous</c> without a valid principal.
    /// </remarks>
    /// <param name="request">The HTTP request carrying the Easy Auth principal header.</param>
    /// <returns>The actor description used in the activity log.</returns>
    public static string GetActor(HttpRequest request)
    {
        var claims = ReadClaims(request);
        if (claims is null)
        {
            return "anonymous";
        }

        string? Find(params string[] types) => claims.FirstOrDefault(claim =>
            types.Contains(claim.Type, StringComparer.OrdinalIgnoreCase)).Value;
        var name = Find(
            "preferred_username",
            "upn",
            "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn",
            "unique_name",
            "name",
            "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name");
        var objectId = Find(ObjectIdClaim, ObjectIdClaimUri);
        return (name, objectId) switch
        {
            (not null, not null) => $"{name} (oid {objectId})",
            (not null, null) => name,
            (null, not null) => $"oid {objectId}",
            _ => "unknown principal"
        };
    }

    /// <summary>Gets the display name of the authenticated principal for the portal header.</summary>
    /// <param name="request">The HTTP request carrying the Easy Auth principal header.</param>
    /// <returns>The display name, else the user principal name, else <c>unknown</c>.</returns>
    public static string GetDisplayName(HttpRequest request)
    {
        var claims = ReadClaims(request);
        string? Find(params string[] types) => claims?.FirstOrDefault(claim =>
            types.Contains(claim.Type, StringComparer.OrdinalIgnoreCase)).Value;
        return Find("name", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name") ??
            Find("preferred_username", "upn", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn") ??
            "unknown";
    }

    /// <summary>Returns whether an Entra principal has at least one required application role.</summary>
    /// <remarks>Role values are compared case-sensitively.</remarks>
    /// <param name="request">The HTTP request carrying the Easy Auth principal header.</param>
    /// <param name="requiredRoles">The application roles of which the principal needs at least one.</param>
    /// <returns>The authorization outcome.</returns>
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

    /// <summary>Decodes and parses the claims of the Easy Auth principal header.</summary>
    /// <remarks>
    /// Malformed Base64 or JSON, a missing or repeated header, a non-Entra provider, and an empty
    /// claim list are treated as unauthenticated rather than raising errors.
    /// </remarks>
    /// <param name="request">The HTTP request carrying the Easy Auth principal header.</param>
    /// <returns>The non-empty claim list, or <see langword="null"/> when no valid Entra principal exists.</returns>
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