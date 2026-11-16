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

using HybridDeviceTagger.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace HybridDeviceTagger;

/// <summary>Exposes forest-domain configuration independently of Active Directory connectivity.</summary>
public sealed partial class ManagementApiFunction
{
    /// <summary>Reads the effective forest-domain configuration.</summary>
    /// <param name="request">The authenticated management request.</param>
    /// <param name="cancellationToken">Cancels authorization and storage access.</param>
    /// <returns>HTTP 200 with the effective domain and version, or 401 or 403.</returns>
    [Function("GetActiveDirectoryConfiguration")]
    public async Task<IActionResult> GetActiveDirectoryConfigurationAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/management/active-directory")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (await AuthorizeAsync(request, cancellationToken,
            ManagementApiRoles.Reader,
            ManagementApiRoles.RuleAdministrator,
            ManagementApiRoles.PasswordRotationAdministrator) is { } failure)
        {
            return failure;
        }
        return ActiveDirectoryConfigurationResult(
            request, await _directoryConfigurationStore.GetAsync(cancellationToken));
    }

    /// <summary>Updates the forest domain without requiring working DNS, LDAP, or a stored credential.</summary>
    /// <remarks>
    /// Requires effective Function App Owner/Contributor access; application roles are not accepted.
    /// The domain selects the servers that receive the service-account password during binds, so
    /// changing it is as sensitive as changing the trust anchors. Only syntax is validated so a
    /// broken forest configuration remains repairable.
    /// </remarks>
    /// <param name="request">The request containing a domain property and optional If-Match header.</param>
    /// <param name="cancellationToken">Cancels authorization and storage access.</param>
    /// <returns>HTTP 200 with the stored configuration, or 400, 401, 403, 409, or 413.</returns>
    [Function("SetActiveDirectoryConfiguration")]
    public async Task<IActionResult> SetActiveDirectoryConfigurationAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "api/management/active-directory")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (await AuthorizeFunctionAppAdministratorAsync(request, cancellationToken) is { } failure)
        {
            return failure;
        }
        string? domain = null;
        var result = await ExecuteConfigurationMutationAsync(request, async () =>
        {
            var body = await ReadJsonAsync<UpdateActiveDirectoryConfigurationRequest>(request, cancellationToken);
            domain = body.Domain;
            var current = await _directoryConfigurationStore.GetAsync(cancellationToken);
            return await _directoryConfigurationStore.ReplaceAsync(
                body.Domain, GetExpectedETag(request) ?? current.ETag, cancellationToken);
        }, ActiveDirectoryConfigurationResult);
        return RecordActivity(request, ActivityCategories.Configuration, "ForestDomainChanged", domain, result);
    }

    /// <summary>Returns the directory configuration with its version header.</summary>
    /// <param name="request">The request whose response receives the ETag.</param>
    /// <param name="snapshot">The effective forest-domain snapshot.</param>
    /// <returns>HTTP 200 with the domain, ETag, modification time, and source.</returns>
    private static IActionResult ActiveDirectoryConfigurationResult(
        HttpRequest request, ActiveDirectoryConfigurationSnapshot snapshot)
    {
        if (snapshot.ETag is not null)
        {
            request.HttpContext.Response.Headers.ETag = snapshot.ETag;
        }
        return new OkObjectResult(snapshot);
    }

    /// <summary>Contains the replacement forest DNS name.</summary>
    /// <param name="Domain">The forest DNS name.</param>
    private sealed record UpdateActiveDirectoryConfigurationRequest(string Domain);
}
