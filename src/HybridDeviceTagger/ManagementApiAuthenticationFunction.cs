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
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/management/authentication")]
        HttpRequest request) =>
        new OkObjectResult(new { tokenScope = _options.TokenScope });
}