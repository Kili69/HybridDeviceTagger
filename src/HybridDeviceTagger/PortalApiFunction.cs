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

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using Azure;
using HybridDeviceTagger.Options;
using HybridDeviceTagger.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger;

/// <summary>Provides the configuration-portal API, restricted to Function App Owners and Contributors.</summary>
/// <remarks>
/// Management API application roles are deliberately not accepted. Every change and every denied
/// request is recorded in the activity log. Passwords are never returned or logged.
/// </remarks>
public sealed class PortalApiFunction
{
    /// <summary>Checks Owner/Contributor access on the Function App.</summary>
    private readonly IManagementApiAuthorizer _authorizer;

    /// <summary>Records portal activities.</summary>
    private readonly IActivityLog _activityLog;

    /// <summary>Reads the activity log.</summary>
    private readonly IActivityLogReader _activityLogReader;

    /// <summary>Manages LDAP trust anchors.</summary>
    private readonly ITrustedCertificateStore _trustStore;

    /// <summary>Validates LDAP access and changes the service account.</summary>
    private readonly IActiveDirectoryAccountService _accountService;

    /// <summary>Reads the forest and service-account configuration.</summary>
    private readonly IActiveDirectoryConfigurationStore _configurationStore;

    /// <summary>The initial Active Directory settings.</summary>
    private readonly ActiveDirectoryOptions _activeDirectoryOptions;

    /// <summary>Searches Entra groups and administrative units for the rule editor.</summary>
    private readonly IEntraDirectorySearch _directorySearch;

    /// <summary>Reads and updates the tagging and password-rotation intervals.</summary>
    private readonly IOperationScheduleStore _scheduleStore;

    /// <summary>Initializes the portal API.</summary>
    /// <param name="authorizer">Checks Owner/Contributor access on the Function App.</param>
    /// <param name="activityLog">Records portal activities.</param>
    /// <param name="activityLogReader">Reads the activity log.</param>
    /// <param name="trustStore">Manages LDAP trust anchors.</param>
    /// <param name="accountService">Validates LDAP access and changes the service account.</param>
    /// <param name="configurationStore">Reads the forest and service-account configuration.</param>
    /// <param name="activeDirectoryOptions">The initial Active Directory settings.</param>
    /// <param name="directorySearch">Searches Entra groups and administrative units.</param>
    /// <param name="scheduleStore">Reads and updates the operation intervals.</param>
    public PortalApiFunction(
        IManagementApiAuthorizer authorizer,
        IActivityLog activityLog,
        IActivityLogReader activityLogReader,
        ITrustedCertificateStore trustStore,
        IActiveDirectoryAccountService accountService,
        IActiveDirectoryConfigurationStore configurationStore,
        IOptions<ActiveDirectoryOptions> activeDirectoryOptions,
        IEntraDirectorySearch directorySearch,
        IOperationScheduleStore scheduleStore)
    {
        _scheduleStore = scheduleStore;
        _directorySearch = directorySearch;
        _authorizer = authorizer;
        _activityLog = activityLog;
        _activityLogReader = activityLogReader;
        _trustStore = trustStore;
        _accountService = accountService;
        _configurationStore = configurationStore;
        _activeDirectoryOptions = activeDirectoryOptions.Value;
    }

    /// <summary>Confirms portal access and records the sign-in.</summary>
    /// <param name="request">The authenticated request.</param>
    /// <param name="cancellationToken">Cancels the RBAC lookup.</param>
    /// <returns>HTTP 200 with the signed-in user, or 401 or 403.</returns>
    [Function("GetPortalSession")]
    public async Task<IActionResult> GetSessionAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "management/portal/session")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (await AuthorizeAsync(request, cancellationToken) is { } failure)
        {
            return failure;
        }

        var actor = EasyAuthPrincipalValidator.GetActor(request);
        Record(request, ActivityCategories.Access, "PortalSignIn", ActivityOutcomes.Succeeded, null, null);
        return new OkObjectResult(new
        {
            user = actor,
            displayName = EasyAuthPrincipalValidator.GetDisplayName(request),
            activityLogAvailable = _activityLogReader.IsConfigured
        });
    }

    /// <summary>Reads the activity log, newest first.</summary>
    /// <remarks>
    /// Query parameters: <c>hours</c> (1-720, default 24), repeated <c>category</c> and
    /// <c>outcome</c> filters, and <c>take</c> (1-1000, default 200).
    /// </remarks>
    /// <param name="request">The authenticated request.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>HTTP 200 with the activities, 400 for invalid filters, 503 when no log is configured, or 401 or 403.</returns>
    [Function("GetPortalActivity")]
    public async Task<IActionResult> GetActivityAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "management/portal/activity")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (await AuthorizeAsync(request, cancellationToken) is { } failure)
        {
            return failure;
        }
        if (!_activityLogReader.IsConfigured)
        {
            return new ObjectResult(new { error = "No Application Insights resource is configured for activity queries." })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
        }

        if (!TryReadInt(request, "hours", 24, 1, 720, out var hours) ||
            !TryReadInt(request, "take", 200, 1, 1000, out var take))
        {
            return new BadRequestObjectResult(new { error = "hours must be 1-720 and take must be 1-1000." });
        }
        var categories = request.Query["category"].Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToArray();
        var outcomes = request.Query["outcome"].Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToArray();
        if (categories.Except(ActivityCategories.All).Any() || outcomes.Except(ActivityOutcomes.All).Any())
        {
            return new BadRequestObjectResult(new
            {
                error = "Unknown category or outcome.",
                categories = ActivityCategories.All,
                outcomes = ActivityOutcomes.All
            });
        }

        IReadOnlyList<ActivityRecord> records;
        try
        {
            records = await _activityLogReader.QueryAsync(
                new ActivityQuery(hours, categories, outcomes, take),
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            return new ObjectResult(new { error = exception.Message })
            {
                StatusCode = StatusCodes.Status502BadGateway
            };
        }
        return new OkObjectResult(new { hours, records });
    }

    /// <summary>Gets the effective forest domain and service account.</summary>
    /// <param name="request">The authenticated request.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 200 with the configuration, or 401 or 403.</returns>
    [Function("GetPortalActiveDirectory")]
    public async Task<IActionResult> GetActiveDirectoryAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "management/portal/active-directory")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (await AuthorizeAsync(request, cancellationToken) is { } failure)
        {
            return failure;
        }

        var snapshot = await _configurationStore.GetAsync(cancellationToken);
        var (username, distinguishedName) = snapshot.GetServiceAccount(_activeDirectoryOptions);
        if (snapshot.ETag is not null)
        {
            request.HttpContext.Response.Headers.ETag = snapshot.ETag;
        }
        return new OkObjectResult(new
        {
            snapshot.Domain,
            username,
            distinguishedName,
            accountSource = string.IsNullOrWhiteSpace(snapshot.BindUsername) ? "appSetting" : "blob",
            snapshot.Source,
            snapshot.ETag,
            snapshot.LastModifiedUtc
        });
    }

    /// <summary>Validates LDAP access with the stored credential and reports every attempt.</summary>
    /// <param name="request">The authenticated request.</param>
    /// <param name="cancellationToken">Cancels the validation.</param>
    /// <returns>HTTP 200 with the result, 409 when no credential is stored, or 401 or 403.</returns>
    [Function("ValidatePortalActiveDirectoryAccess")]
    public async Task<IActionResult> ValidateActiveDirectoryAccessAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "management/portal/active-directory/validate")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (await AuthorizeAsync(request, cancellationToken) is { } failure)
        {
            return failure;
        }

        try
        {
            var result = await _accountService.ValidateCurrentAsync(cancellationToken);
            Record(
                request,
                ActivityCategories.Password,
                "LdapAccessValidated",
                result.Succeeded ? ActivityOutcomes.Succeeded : ActivityOutcomes.Failed,
                result.Attempts.LastOrDefault()?.Server,
                result.Error);
            return new OkObjectResult(result);
        }
        catch (ActiveDirectoryCredentialNotInitializedException exception)
        {
            Record(request, ActivityCategories.Password, "LdapAccessValidated", ActivityOutcomes.Failed, null, exception.Message);
            return new ConflictObjectResult(new { error = exception.Message });
        }
    }

    /// <summary>Switches the service account, or replaces its password, after an LDAP bind validation.</summary>
    /// <remarks>Body: <c>username</c>, <c>password</c>, optional <c>distinguishedName</c>. Honors <c>If-Match</c> implicitly through the stored version.</remarks>
    /// <param name="request">The authenticated request.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns>HTTP 200 with the new account, 400, 409, 413, 401, or 403.</returns>
    [Function("ChangePortalActiveDirectoryAccount")]
    public async Task<IActionResult> ChangeActiveDirectoryAccountAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "management/portal/active-directory/account")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (await AuthorizeAsync(request, cancellationToken) is { } failure)
        {
            return failure;
        }

        string? username = null;
        var result = await ExecuteAsync(async () =>
        {
            var body = await ManagementApiFunction.ReadJsonAsync<ChangeAccountRequest>(request, cancellationToken);
            username = body.Username;
            var stored = await _accountService.ChangeAccountAsync(
                body.Username ?? string.Empty,
                body.Password ?? string.Empty,
                body.DistinguishedName,
                cancellationToken);
            var (effectiveUsername, distinguishedName) = stored.GetServiceAccount(_activeDirectoryOptions);
            return new OkObjectResult(new { username = effectiveUsername, distinguishedName, stored.ETag });
        });
        return Record(request, ActivityCategories.Configuration, "ServiceAccountChanged", username, result);
    }

    /// <summary>Lists the LDAP trust anchors.</summary>
    /// <param name="request">The authenticated request.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 200 with the trust anchors, or 401 or 403.</returns>
    [Function("GetPortalCertificates")]
    public async Task<IActionResult> GetCertificatesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "management/portal/certificates")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (await AuthorizeAsync(request, cancellationToken) is { } failure)
        {
            return failure;
        }

        return CertificateResult(request, await _trustStore.GetAsync(cancellationToken));
    }

    /// <summary>Adds an LDAP trust anchor.</summary>
    /// <remarks>Body: <c>certificate</c> as PEM or Base64 DER. Honors an optional <c>If-Match</c> header.</remarks>
    /// <param name="request">The authenticated request.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 200 with the trust anchors, or 400, 409, 413, 401, or 403.</returns>
    [Function("AddPortalCertificate")]
    public async Task<IActionResult> AddCertificateAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "management/portal/certificates")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (await AuthorizeAsync(request, cancellationToken) is { } failure)
        {
            return failure;
        }

        string? thumbprint = null;
        var result = await ExecuteAsync(async () =>
        {
            var body = await ManagementApiFunction.ReadJsonAsync<CertificateRequest>(request, cancellationToken);
            var before = await _trustStore.GetAsync(cancellationToken);
            var snapshot = await _trustStore.AddAsync(
                body.Certificate ?? string.Empty,
                EasyAuthPrincipalValidator.GetActor(request),
                ManagementApiFunction.GetExpectedETag(request) ?? before.ETag,
                cancellationToken);
            thumbprint = snapshot.Certificates.Select(certificate => certificate.Thumbprint)
                .Except(before.Certificates.Select(certificate => certificate.Thumbprint)).FirstOrDefault();
            return CertificateResult(request, snapshot);
        });
        return Record(request, ActivityCategories.Configuration, "TrustAnchorAdded", thumbprint, result);
    }

    /// <summary>Replaces an LDAP trust anchor, for example with a renewed root certificate.</summary>
    /// <param name="request">The authenticated request whose body contains <c>certificate</c>.</param>
    /// <param name="thumbprint">The thumbprint of the certificate to replace.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 200 with the trust anchors, or 400, 404, 409, 413, 401, or 403.</returns>
    [Function("ReplacePortalCertificate")]
    public async Task<IActionResult> ReplaceCertificateAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "management/portal/certificates/{thumbprint}")]
        HttpRequest request,
        string thumbprint,
        CancellationToken cancellationToken)
    {
        if (await AuthorizeAsync(request, cancellationToken) is { } failure)
        {
            return failure;
        }

        var result = await ExecuteAsync(async () =>
        {
            var body = await ManagementApiFunction.ReadJsonAsync<CertificateRequest>(request, cancellationToken);
            var before = await _trustStore.GetAsync(cancellationToken);
            return CertificateResult(request, await _trustStore.ReplaceAsync(
                thumbprint,
                body.Certificate ?? string.Empty,
                EasyAuthPrincipalValidator.GetActor(request),
                ManagementApiFunction.GetExpectedETag(request) ?? before.ETag,
                cancellationToken));
        });
        return Record(request, ActivityCategories.Configuration, "TrustAnchorReplaced", thumbprint, result);
    }

    /// <summary>Removes an LDAP trust anchor; the last one cannot be removed.</summary>
    /// <param name="request">The authenticated request.</param>
    /// <param name="thumbprint">The thumbprint of the certificate to remove.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 200 with the trust anchors, or 400, 404, 409, 401, or 403.</returns>
    [Function("RemovePortalCertificate")]
    public async Task<IActionResult> RemoveCertificateAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "management/portal/certificates/{thumbprint}")]
        HttpRequest request,
        string thumbprint,
        CancellationToken cancellationToken)
    {
        if (await AuthorizeAsync(request, cancellationToken) is { } failure)
        {
            return failure;
        }

        var result = await ExecuteAsync(async () =>
        {
            var before = await _trustStore.GetAsync(cancellationToken);
            return CertificateResult(request, await _trustStore.RemoveAsync(
                thumbprint,
                ManagementApiFunction.GetExpectedETag(request) ?? before.ETag,
                cancellationToken));
        });
        return Record(request, ActivityCategories.Configuration, "TrustAnchorRemoved", thumbprint, result);
    }

    /// <summary>Searches Entra security groups or administrative units by display-name prefix.</summary>
    /// <remarks>Query parameter <c>search</c>: 1 to 64 characters. Non-assignable objects carry a note.</remarks>
    /// <param name="request">The authenticated request.</param>
    /// <param name="kind"><c>groups</c> or <c>administrative-units</c>.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>HTTP 200 with up to 25 matches, 400, 404, 401, or 403.</returns>
    [Function("SearchPortalDirectory")]
    public async Task<IActionResult> SearchDirectoryAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "management/portal/directory/{kind}")]
        HttpRequest request,
        string kind,
        CancellationToken cancellationToken)
    {
        if (await AuthorizeAsync(request, cancellationToken) is { } failure)
        {
            return failure;
        }

        var text = request.Query["search"].ToString().Trim();
        if (text.Length is 0 or > 64)
        {
            return new BadRequestObjectResult(new { error = "search must contain 1 to 64 characters." });
        }
        return kind switch
        {
            "groups" => new OkObjectResult(await _directorySearch.SearchGroupsAsync(text, cancellationToken)),
            "administrative-units" => new OkObjectResult(await _directorySearch.SearchAdministrativeUnitsAsync(text, cancellationToken)),
            _ => new NotFoundObjectResult(new { error = "Unknown directory object kind." })
        };
    }

    /// <summary>Resolves group and administrative-unit IDs to display names for the rule view.</summary>
    /// <remarks>Body: <c>groupIds</c> and <c>administrativeUnitIds</c>, together at most 200 IDs.</remarks>
    /// <param name="request">The authenticated request.</param>
    /// <param name="cancellationToken">Cancels the lookups.</param>
    /// <returns>HTTP 200 with the summaries of existing objects, 400, 413, 401, or 403.</returns>
    [Function("ResolvePortalDirectory")]
    public async Task<IActionResult> ResolveDirectoryAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "management/portal/directory/resolve")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (await AuthorizeAsync(request, cancellationToken) is { } failure)
        {
            return failure;
        }

        return await ExecuteAsync(async () =>
        {
            var body = await ManagementApiFunction.ReadJsonAsync<ResolveDirectoryRequest>(request, cancellationToken);
            var groupIds = body.GroupIds ?? [];
            var administrativeUnitIds = body.AdministrativeUnitIds ?? [];
            if (groupIds.Count + administrativeUnitIds.Count > 200)
            {
                throw new ValidationException("At most 200 IDs can be resolved per request.");
            }
            var resolved = await _directorySearch.ResolveAsync(groupIds, administrativeUnitIds, cancellationToken);
            return new OkObjectResult(resolved.Values);
        });
    }

    /// <summary>Gets the tagging and password-rotation intervals.</summary>
    /// <param name="request">The authenticated request.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 200 with the intervals, or 401 or 403.</returns>
    [Function("GetPortalSchedule")]
    public async Task<IActionResult> GetScheduleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "management/portal/schedule")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (await AuthorizeAsync(request, cancellationToken) is { } failure)
        {
            return failure;
        }

        return ScheduleResult(request, await _scheduleStore.GetAsync(cancellationToken));
    }

    /// <summary>Changes the tagging and password-rotation intervals without a restart.</summary>
    /// <remarks>Body: <c>taggingIntervalMinutes</c> (5-1440) and <c>passwordRotationDays</c>. Honors <c>If-Match</c>.</remarks>
    /// <param name="request">The authenticated request.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 200 with the stored intervals, or 400, 409, 413, 401, or 403.</returns>
    [Function("SetPortalSchedule")]
    public async Task<IActionResult> SetScheduleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "management/portal/schedule")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (await AuthorizeAsync(request, cancellationToken) is { } failure)
        {
            return failure;
        }

        string? target = null;
        var result = await ExecuteAsync(async () =>
        {
            var body = await ManagementApiFunction.ReadJsonAsync<ScheduleRequest>(request, cancellationToken);
            target = $"tagging every {body.TaggingIntervalMinutes} min, password rotation every {body.PasswordRotationDays} days";
            var current = await _scheduleStore.GetAsync(cancellationToken);
            return ScheduleResult(request, await _scheduleStore.ReplaceAsync(
                body.TaggingIntervalMinutes,
                body.PasswordRotationDays,
                ManagementApiFunction.GetExpectedETag(request) ?? current.ETag,
                cancellationToken));
        });
        return Record(request, ActivityCategories.Configuration, "ScheduleChanged", target, result);
    }

    /// <summary>Returns the intervals with their version header.</summary>
    /// <param name="request">The request whose response receives the ETag.</param>
    /// <param name="snapshot">The intervals.</param>
    /// <returns>HTTP 200 with the snapshot.</returns>
    private static IActionResult ScheduleResult(HttpRequest request, OperationScheduleSnapshot snapshot)
    {
        if (snapshot.ETag is not null)
        {
            request.HttpContext.Response.Headers.ETag = snapshot.ETag;
        }
        return new OkObjectResult(snapshot);
    }

    /// <summary>Authorizes Owner/Contributor access and records denials.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancels the RBAC lookup.</param>
    /// <returns>Null when authorized; otherwise the 401 or 403 result.</returns>
    private async Task<IActionResult?> AuthorizeAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var result = await _authorizer.AuthorizeFunctionAppAdministratorAsync(request, cancellationToken);
        if (result == EasyAuthAuthorizationResult.Authorized)
        {
            return null;
        }

        Record(
            request,
            ActivityCategories.Access,
            "PortalAccessDenied",
            ActivityOutcomes.Denied,
            $"{request.Method} {request.Path}",
            "The configuration portal requires Owner or Contributor on the Function App.");
        return result == EasyAuthAuthorizationResult.Forbidden
            ? new ObjectResult(new { error = "The configuration portal requires the Owner or Contributor role on the Function App." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            }
            : new UnauthorizedObjectResult(new { error = "Microsoft Entra authentication is required." });
    }

    /// <summary>Runs a change and maps expected failures to HTTP results.</summary>
    /// <param name="change">The change returning its success result.</param>
    /// <returns>The success result or the mapped failure.</returns>
    private static async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> change)
    {
        try
        {
            return await change();
        }
        catch (ValidationException exception)
        {
            return new BadRequestObjectResult(new { error = exception.Message });
        }
        catch (JsonException exception)
        {
            return new BadRequestObjectResult(new { error = "The request body is not valid JSON.", detail = exception.Message });
        }
        catch (ActiveDirectoryCredentialValidationException exception)
        {
            return new BadRequestObjectResult(new { error = exception.Message, attempts = exception.Validation.Attempts });
        }
        catch (KeyNotFoundException exception)
        {
            return new NotFoundObjectResult(new { error = exception.Message });
        }
        catch (OperationAlreadyRunningException exception)
        {
            return new ConflictObjectResult(new { error = exception.Message });
        }
        catch (RequestFailedException exception) when (exception.Status is 409 or 412)
        {
            return new ConflictObjectResult(new { error = "The configuration changed concurrently. Reload and try again." });
        }
        catch (ManagementApiFunction.RequestBodyTooLargeException)
        {
            return new ObjectResult(new { error = $"The request body cannot exceed {ManagementApiFunction.MaximumRequestBytes} bytes." })
            {
                StatusCode = StatusCodes.Status413PayloadTooLarge
            };
        }
    }

    /// <summary>Records a change result and returns it unchanged.</summary>
    /// <param name="request">The request identifying the actor.</param>
    /// <param name="category">The activity category.</param>
    /// <param name="action">The action name.</param>
    /// <param name="target">The affected object.</param>
    /// <param name="result">The HTTP result.</param>
    /// <returns><paramref name="result"/>.</returns>
    private IActionResult Record(HttpRequest request, string category, string action, string? target, IActionResult result)
    {
        var failed = result is ObjectResult { StatusCode: >= 400 } or BadRequestObjectResult or NotFoundObjectResult or ConflictObjectResult;
        var error = failed && result is ObjectResult { Value: { } value }
            ? JsonSerializer.Serialize(value)
            : null;
        Record(request, category, action, failed ? ActivityOutcomes.Failed : ActivityOutcomes.Succeeded, target, error);
        return result;
    }

    /// <summary>Writes one activity entry for the request principal.</summary>
    /// <param name="request">The request identifying the actor.</param>
    /// <param name="category">The activity category.</param>
    /// <param name="action">The action name.</param>
    /// <param name="outcome">The outcome.</param>
    /// <param name="target">The affected object.</param>
    /// <param name="details">Additional non-secret details.</param>
    private void Record(HttpRequest request, string category, string action, string outcome, string? target, string? details) =>
        _activityLog.Record(new ActivityEntry(
            category, action, outcome, EasyAuthPrincipalValidator.GetActor(request), target, details));

    /// <summary>Returns the trust anchors with their version header.</summary>
    /// <param name="request">The request whose response receives the ETag.</param>
    /// <param name="snapshot">The trust anchors.</param>
    /// <returns>HTTP 200 with the snapshot.</returns>
    private static IActionResult CertificateResult(HttpRequest request, TrustedCertificateSnapshot snapshot)
    {
        if (snapshot.ETag is not null)
        {
            request.HttpContext.Response.Headers.ETag = snapshot.ETag;
        }
        return new OkObjectResult(snapshot);
    }

    /// <summary>Reads a bounded integer query parameter.</summary>
    /// <param name="request">The request.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="defaultValue">The value used when the parameter is absent.</param>
    /// <param name="minimum">The inclusive minimum.</param>
    /// <param name="maximum">The inclusive maximum.</param>
    /// <param name="value">The parsed value.</param>
    /// <returns><see langword="true"/> when absent or valid.</returns>
    private static bool TryReadInt(HttpRequest request, string name, int defaultValue, int minimum, int maximum, out int value)
    {
        var text = request.Query[name].ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            value = defaultValue;
            return true;
        }
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
            value >= minimum && value <= maximum;
    }

    /// <summary>The account-change request body.</summary>
    /// <param name="Username">The service-account UPN.</param>
    /// <param name="Password">The current password of the account.</param>
    /// <param name="DistinguishedName">The optional DN.</param>
    private sealed record ChangeAccountRequest(string? Username, string? Password, string? DistinguishedName);

    /// <summary>The schedule request body.</summary>
    /// <param name="TaggingIntervalMinutes">Minutes between scheduled tagging runs.</param>
    /// <param name="PasswordRotationDays">Password age in days at which rotation is due.</param>
    private sealed record ScheduleRequest(int TaggingIntervalMinutes, int PasswordRotationDays);

    /// <summary>The directory-resolution request body.</summary>
    /// <param name="GroupIds">The group IDs.</param>
    /// <param name="AdministrativeUnitIds">The administrative-unit IDs.</param>
    private sealed record ResolveDirectoryRequest(List<Guid>? GroupIds, List<Guid>? AdministrativeUnitIds);

    /// <summary>The certificate request body.</summary>
    /// <param name="Certificate">The PEM or Base64 DER public certificate.</param>
    private sealed record CertificateRequest(string? Certificate);
}
