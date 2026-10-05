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
    Task<EasyAuthAuthorizationResult> AuthorizeAsync(
        HttpRequest request,
        IReadOnlyCollection<string> requiredRoles,
        CancellationToken cancellationToken);
}

/// <summary>Combines management API application roles with Function App Owner and Contributor RBAC.</summary>
public sealed class ManagementApiAuthorizer : IManagementApiAuthorizer
{
    private const string ArmScope = "https://management.azure.com/.default";
    private const string OwnerRoleDefinitionId = "8e3af657-a8ff-443c-a75c-2fe8c4bcb635";
    private const string ContributorRoleDefinitionId = "b24988ac-6180-42a0-ab88-20f7382dd24c";
    private static readonly HashSet<string> RbacAdministrativeRoles =
    [
        ManagementApiRoles.RuleAdministrator,
        ManagementApiRoles.PasswordRotationAdministrator
    ];
    private static readonly HashSet<string> AcceptedRoleDefinitionIds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            OwnerRoleDefinitionId,
            ContributorRoleDefinitionId
        };
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private readonly string _functionAppResourceId;

    /// <summary>Initializes the authorizer.</summary>
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

    private async Task<bool> HasFunctionAppOwnerOrContributorRoleAsync(
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
                    AcceptedRoleDefinitionIds.Contains(roleId)))
            {
                return true;
            }

            nextLink = page.NextLink;
        }

        return false;
    }

    private static bool TryGetRoleDefinitionId(string roleDefinitionId, out string roleId)
    {
        var separatorIndex = roleDefinitionId.LastIndexOf('/');
        roleId = separatorIndex >= 0
            ? roleDefinitionId[(separatorIndex + 1)..]
            : roleDefinitionId;
        return Guid.TryParse(roleId, out _);
    }

    private sealed record RoleAssignmentPage(
        IReadOnlyList<RoleAssignment> Value,
        string? NextLink);

    private sealed record RoleAssignment(RoleAssignmentProperties Properties);

    private sealed record RoleAssignmentProperties(string RoleDefinitionId);
}
