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

using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using HybridDeviceTagger.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Services;

/// <summary>Authorizes management API requests through application roles or Azure RBAC.</summary>
public interface IManagementApiAuthorizer
{
    /// <summary>Determines whether the request principal may use an operation.</summary>
    /// <param name="request">The HTTP request carrying the App Service Easy Auth principal header.</param>
    /// <param name="requiredRoles">The application roles of which the principal needs at least one.</param>
    /// <param name="cancellationToken">Cancels a required Azure RBAC lookup.</param>
    /// <returns>The authorization outcome for the request principal.</returns>
    Task<EasyAuthAuthorizationResult> AuthorizeAsync(
        HttpRequest request,
        IReadOnlyCollection<string> requiredRoles,
        CancellationToken cancellationToken);

    /// <summary>Determines whether the request principal holds Owner or Contributor on the Function App.</summary>
    /// <remarks>Application roles are deliberately not accepted; this protects the configuration portal.</remarks>
    /// <param name="request">The HTTP request carrying the App Service Easy Auth principal header.</param>
    /// <param name="cancellationToken">Cancels the Azure RBAC lookup.</param>
    /// <returns>The authorization outcome for the request principal.</returns>
    Task<EasyAuthAuthorizationResult> AuthorizeFunctionAppAdministratorAsync(
        HttpRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Combines management API application roles with Function App Owner and Contributor RBAC.</summary>
/// <remarks>
/// Application roles are evaluated first. Only when an authenticated principal lacks them and the
/// endpoint requires an administrative role (rule administration, tagging operation, or password-rotation administration) does the
/// authorizer query Azure RBAC with the Function App managed identity. Owner or Contributor
/// assignments that apply to the Function App, including inherited ones, then grant access.
/// </remarks>
public sealed class ManagementApiAuthorizer : IManagementApiAuthorizer
{
    /// <summary>The Azure Resource Manager token scope.</summary>
    private const string ArmScope = "https://management.azure.com/.default";

    /// <summary>The built-in Azure Owner role definition ID.</summary>
    private const string OwnerRoleDefinitionId = "8e3af657-a8ff-443c-a75c-2fe8c4bcb635";

    /// <summary>The built-in Azure Contributor role definition ID.</summary>
    private const string ContributorRoleDefinitionId = "b24988ac-6180-42a0-ab88-20f7382dd24c";

    /// <summary>The application roles that Owner or Contributor RBAC assignments can substitute.</summary>
    private static readonly HashSet<string> RbacAdministrativeRoles =
    [
        ManagementApiRoles.RuleAdministrator,
        ManagementApiRoles.TaggingOperator,
        ManagementApiRoles.PasswordRotationAdministrator
    ];

    /// <summary>The role definition IDs that grant administrative management API access.</summary>
    private static readonly HashSet<string> AcceptedRoleDefinitionIds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            OwnerRoleDefinitionId,
            ContributorRoleDefinitionId
        };

    /// <summary>Web JSON defaults used to read Azure Resource Manager responses.</summary>
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    /// <summary>How long an Owner/Contributor lookup result is reused.</summary>
    private static readonly TimeSpan RbacCacheDuration = TimeSpan.FromSeconds(60);

    /// <summary>Recent Owner/Contributor lookup results per principal object ID.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, (bool Allowed, DateTimeOffset ExpiresUtc)> _rbacCache = new();

    /// <summary>Sends Azure Resource Manager role-assignment requests.</summary>
    private readonly HttpClient _httpClient;

    /// <summary>Acquires Azure Resource Manager tokens for the Function App identity.</summary>
    private readonly TokenCredential _credential;

    /// <summary>The Function App resource ID without a trailing slash.</summary>
    private readonly string _functionAppResourceId;

    /// <summary>Initializes the authorizer.</summary>
    /// <param name="httpClient">Sends Azure Resource Manager role-assignment requests.</param>
    /// <param name="credential">Acquires Azure Resource Manager tokens for the Function App identity.</param>
    /// <param name="options">Provides the Function App resource ID used as the RBAC scope.</param>
    public ManagementApiAuthorizer(
        HttpClient httpClient,
        TokenCredential credential,
        IOptions<ManagementAuthorizationOptions> options)
    {
        _httpClient = httpClient;
        _credential = credential;
        _functionAppResourceId = options.Value.FunctionAppResourceId.TrimEnd('/');
    }

    /// <inheritdoc />
    public async Task<EasyAuthAuthorizationResult> AuthorizeAsync(
        HttpRequest request,
        IReadOnlyCollection<string> requiredRoles,
        CancellationToken cancellationToken)
    {
        var applicationRoleResult = EasyAuthPrincipalValidator.Authorize(
            request,
            [.. requiredRoles]);
        if (applicationRoleResult != EasyAuthAuthorizationResult.Forbidden ||
            !requiredRoles.Any(RbacAdministrativeRoles.Contains))
        {
            return applicationRoleResult;
        }

        if (!EasyAuthPrincipalValidator.TryGetObjectId(request, out var objectId))
        {
            return EasyAuthAuthorizationResult.Forbidden;
        }

        return await HasFunctionAppOwnerOrContributorRoleAsync(objectId, cancellationToken)
            ? EasyAuthAuthorizationResult.Authorized
            : EasyAuthAuthorizationResult.Forbidden;
    }

    /// <inheritdoc />
    public async Task<EasyAuthAuthorizationResult> AuthorizeFunctionAppAdministratorAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!EasyAuthPrincipalValidator.IsAuthenticated(request))
        {
            return EasyAuthAuthorizationResult.Unauthenticated;
        }
        if (!EasyAuthPrincipalValidator.TryGetObjectId(request, out var objectId))
        {
            return EasyAuthAuthorizationResult.Forbidden;
        }

        return await HasFunctionAppOwnerOrContributorRoleAsync(objectId, cancellationToken)
            ? EasyAuthAuthorizationResult.Authorized
            : EasyAuthAuthorizationResult.Forbidden;
    }

    /// <summary>Checks Owner or Contributor on the Function App, caching results briefly.</summary>
    /// <remarks>
    /// The configuration portal issues several requests per view. Caching each principal's result
    /// for <see cref="RbacCacheDuration"/> avoids repeated Azure Resource Manager lookups while
    /// still applying granted or revoked role assignments shortly afterwards.
    /// </remarks>
    /// <param name="objectId">The Microsoft Entra object ID of the principal.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns><see langword="true"/> when an accepted role assignment exists.</returns>
    private async Task<bool> HasFunctionAppOwnerOrContributorRoleAsync(
        Guid objectId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (_rbacCache.TryGetValue(objectId, out var cached) && cached.ExpiresUtc > now)
        {
            return cached.Allowed;
        }

        var allowed = await QueryFunctionAppOwnerOrContributorRoleAsync(objectId, cancellationToken);
        _rbacCache[objectId] = (allowed, now.Add(RbacCacheDuration));
        return allowed;
    }

    /// <summary>Checks whether a principal holds Owner or Contributor on the Function App.</summary>
    /// <remarks>
    /// Uses the <c>assignedTo</c> filter, which includes group-based and inherited assignments,
    /// and follows <c>nextLink</c> pages until a match is found.
    /// </remarks>
    /// <param name="objectId">The Microsoft Entra object ID of the principal.</param>
    /// <param name="cancellationToken">Cancels token acquisition and the RBAC requests.</param>
    /// <returns><see langword="true"/> when an accepted role assignment exists.</returns>
    /// <exception cref="HttpRequestException">Azure Resource Manager returned an unsuccessful status code.</exception>
    /// <exception cref="JsonException">Azure Resource Manager returned an empty or invalid response.</exception>
    private async Task<bool> QueryFunctionAppOwnerOrContributorRoleAsync(
        Guid objectId,
        CancellationToken cancellationToken)
    {
        var filter = Uri.EscapeDataString($"assignedTo('{objectId:D}')");
        var nextLink =
            $"https://management.azure.com{_functionAppResourceId}" +
            "/providers/Microsoft.Authorization/roleAssignments" +
            $"?api-version=2022-04-01&$filter={filter}";
        var token = await _credential.GetTokenAsync(
            new TokenRequestContext([ArmScope]),
            cancellationToken);

        while (!string.IsNullOrWhiteSpace(nextLink))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, nextLink);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            var page = await JsonSerializer.DeserializeAsync<RoleAssignmentPage>(
                content,
                SerializerOptions,
                cancellationToken: cancellationToken) ??
                throw new JsonException("Azure RBAC returned an empty response.");

            if (page.Value.Any(assignment =>
                    TryGetRoleDefinitionId(assignment.Properties.RoleDefinitionId, out var roleId) &&
                    AcceptedRoleDefinitionIds.Contains(roleId) &&
                    AppliesToFunctionApp(assignment.Properties.Scope)))
            {
                return true;
            }

            nextLink = page.NextLink;
        }

        return false;
    }

    /// <summary>Returns whether an assignment scope is the Function App or one of its parents.</summary>
    /// <remarks>
    /// Assignments on child resources of the Function App must not grant access to the app itself.
    /// Management-group scopes are parents of every subscription and are accepted.
    /// </remarks>
    /// <param name="scope">The assignment scope.</param>
    /// <returns><see langword="true"/> when the assignment covers the Function App.</returns>
    internal bool AppliesToFunctionApp(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return false;
        }

        var normalized = scope.TrimEnd('/');
        return normalized.Length == 0 ||
            string.Equals(normalized, _functionAppResourceId, StringComparison.OrdinalIgnoreCase) ||
            _functionAppResourceId.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("/providers/Microsoft.Management/managementGroups/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Extracts the GUID from a fully qualified role definition resource ID.</summary>
    /// <param name="roleDefinitionId">The role definition resource ID or bare GUID.</param>
    /// <param name="roleId">The final path segment of <paramref name="roleDefinitionId"/>.</param>
    /// <returns><see langword="true"/> when the final segment is a GUID.</returns>
    private static bool TryGetRoleDefinitionId(string roleDefinitionId, out string roleId)
    {
        var separatorIndex = roleDefinitionId.LastIndexOf('/');
        roleId = separatorIndex >= 0
            ? roleDefinitionId[(separatorIndex + 1)..]
            : roleDefinitionId;
        return Guid.TryParse(roleId, out _);
    }

    /// <summary>Represents one page of an Azure role-assignment list response.</summary>
    /// <param name="Value">The role assignments on this page.</param>
    /// <param name="NextLink">The URI of the next page, or <see langword="null"/> on the last page.</param>
    private sealed record RoleAssignmentPage(
        IReadOnlyList<RoleAssignment> Value,
        string? NextLink);

    /// <summary>Represents one Azure role assignment.</summary>
    /// <param name="Properties">The role assignment properties.</param>
    private sealed record RoleAssignment(RoleAssignmentProperties Properties);

    /// <summary>Represents the role assignment properties used for authorization.</summary>
    /// <param name="RoleDefinitionId">The fully qualified role definition resource ID.</param>
    /// <param name="Scope">The scope at which the role is assigned.</param>
    private sealed record RoleAssignmentProperties(string RoleDefinitionId, string? Scope);
}
