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

using System.Reflection;
using HybridDeviceTagger.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger;

/// <summary>Serves the static configuration portal and its public sign-in settings.</summary>
/// <remarks>
/// These paths are excluded from App Service authentication so the browser can load the page
/// before signing in. They contain no data; every portal API call requires an Entra access token
/// and Function App Owner or Contributor access. A strict Content Security Policy permits only
/// same-origin scripts and styles plus token requests to the Entra sign-in host.
/// </remarks>
public sealed class PortalFunction
{
    /// <summary>The embedded static files by request name.</summary>
    private static readonly IReadOnlyDictionary<string, (string Resource, string ContentType)> Files =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            [""] = ("HybridDeviceTagger.Portal.index.html", "text/html; charset=utf-8"),
            ["index.html"] = ("HybridDeviceTagger.Portal.index.html", "text/html; charset=utf-8"),
            ["app.js"] = ("HybridDeviceTagger.Portal.app.js", "text/javascript; charset=utf-8"),
            ["app.css"] = ("HybridDeviceTagger.Portal.app.css", "text/css; charset=utf-8")
        };

    /// <summary>The product version and optional branch, parsed from <c>&lt;YYYY&gt;&lt;MM&gt;.&lt;counter&gt;[-&lt;branch&gt;]</c>.</summary>
    private static readonly (string Version, string? Branch) ProductVersion = ParseProductVersion(
        typeof(PortalFunction).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>The public management API sign-in settings.</summary>
    private readonly ManagementApiOptions _options;

    /// <summary>Initializes the portal file server.</summary>
    /// <param name="options">The public management API sign-in settings.</param>
    public PortalFunction(IOptions<ManagementApiOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>The fixed portal address; the redirect never uses request input to avoid open redirects.</summary>
    internal const string PortalPath = "/api/portal";

    /// <summary>Redirects the Function App root address to the configuration portal.</summary>
    /// <remarks>
    /// Only the exact root path is excluded from App Service authentication. The response is a
    /// fixed redirect without data; the portal then requires Entra sign-in, and every portal API
    /// call still requires an Entra access token and Function App Owner or Contributor access.
    /// </remarks>
    /// <param name="request">The anonymous request for the root path.</param>
    /// <returns>HTTP 302 to <see cref="PortalPath"/>.</returns>
    [Function("RedirectRootToPortal")]
    public IActionResult RedirectRoot(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "{ignored:maxlength(0)?}")]
        HttpRequest request)
    {
        ApplySecurityHeaders(request.HttpContext.Response, _options.AuthorityHost);
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        return new RedirectResult(PortalPath, permanent: false);
    }

    /// <summary>Serves a portal file or the public sign-in configuration.</summary>
    /// <param name="request">The anonymous request.</param>
    /// <param name="file">The requested file name; empty serves the page.</param>
    /// <returns>The file, the JSON configuration, or HTTP 404.</returns>
    [Function("GetPortalFile")]
    public IActionResult GetFile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/portal/{file?}")]
        HttpRequest request,
        string? file)
    {
        ApplySecurityHeaders(request.HttpContext.Response, _options.AuthorityHost);
        if (string.Equals(file, "config.json", StringComparison.Ordinal))
        {
            request.HttpContext.Response.Headers.CacheControl = "no-store";
            if (string.IsNullOrWhiteSpace(_options.TenantId))
            {
                return new ObjectResult(new { error = "ManagementApi__TenantId is not configured." })
                {
                    StatusCode = StatusCodes.Status503ServiceUnavailable
                };
            }
            return new OkObjectResult(new
            {
                clientId = _options.ClientId,
                tenantId = _options.TenantId,
                authorityHost = _options.AuthorityHost.TrimEnd('/'),
                scope = _options.PortalScope,
                version = ProductVersion.Version,
                branch = ProductVersion.Branch
            });
        }

        if (!Files.TryGetValue(file ?? string.Empty, out var entry))
        {
            return new NotFoundResult();
        }

        var stream = typeof(PortalFunction).Assembly.GetManifestResourceStream(entry.Resource);
        if (stream is null)
        {
            return new NotFoundResult();
        }
        request.HttpContext.Response.Headers.CacheControl = "no-cache";
        return new FileStreamResult(stream, entry.ContentType);
    }

    /// <summary>Splits the informational version into the product version and the branch.</summary>
    /// <remarks>Builds from <c>main</c> carry no suffix, so the branch is null and not displayed.</remarks>
    /// <param name="informationalVersion">The informational version, for example <c>202611.1-dev</c>.</param>
    /// <returns>The version and the branch, or null when built from <c>main</c>.</returns>
    internal static (string Version, string? Branch) ParseProductVersion(string? informationalVersion)
    {
        var value = (informationalVersion ?? "unknown").Split('+')[0];
        var dash = value.IndexOf('-', StringComparison.Ordinal);
        return dash < 0 ? (value, null) : (value[..dash], value[(dash + 1)..]);
    }

    /// <summary>Applies the Content Security Policy and related browser protections.</summary>
    /// <param name="response">The response.</param>
    /// <param name="authorityHost">The Entra sign-in host that receives token requests.</param>
    internal static void ApplySecurityHeaders(HttpResponse response, string authorityHost)
    {
        var host = authorityHost.TrimEnd('/');
        response.Headers.ContentSecurityPolicy =
            "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
            $"connect-src 'self' {host}; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.XFrameOptions = "DENY";
        response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
