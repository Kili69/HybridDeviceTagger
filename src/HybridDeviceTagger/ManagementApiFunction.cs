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
using System.ComponentModel.DataAnnotations;
using Azure;
using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using HybridDeviceTagger.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Azure.Functions.Worker;

namespace HybridDeviceTagger;

/// <summary>Provides the Entra-authenticated operational management API.</summary>
/// <remarks>
/// Every endpoint uses anonymous Function-level authorization because App Service Easy Auth
/// authenticates the caller in front of the worker; each endpoint then authorizes the
/// Easy Auth principal against its required application roles or Azure RBAC assignments.
/// </remarks>
public sealed partial class ManagementApiFunction
{
    /// <summary>The maximum accepted request body size in bytes.</summary>
    internal const int MaximumRequestBytes = 65536;

    /// <summary>Web JSON defaults with case-insensitive property matching for request bodies.</summary>
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    /// <summary>Validates and persists tagging-rule mutations.</summary>
    private readonly TaggingConfigurationService _configurationService;

    /// <summary>Reads and records operation run statuses.</summary>
    private readonly IOperationStatusStore _statusStore;

    /// <summary>Opens the latest device-tagging trace.</summary>
    private readonly IRunTraceReader _traceReader;

    /// <summary>Detects whether an operation is already running.</summary>
    private readonly IOperationLock _operationLock;

    /// <summary>Queues run requests for the queue-triggered workers.</summary>
    private readonly IOperationRequestQueue _requestQueue;

    /// <summary>Supplies the request timestamp of queued operations.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>Validates and stores the initial Active Directory credential.</summary>
    private readonly IActiveDirectoryCredentialInitializer _credentialInitializer;

    /// <summary>Authorizes the Easy Auth principal of each request.</summary>
    private readonly IManagementApiAuthorizer _authorizer;

    /// <summary>Reads and updates the shared forest-domain configuration.</summary>
    private readonly IActiveDirectoryConfigurationStore _directoryConfigurationStore;

    /// <summary>Records configuration changes, requested runs, and denied requests.</summary>
    private readonly IActivityLog _activityLog;

    /// <summary>Initializes the management API.</summary>
    /// <param name="configurationService">Validates and persists tagging-rule mutations.</param>
    /// <param name="statusStore">Reads and records operation run statuses.</param>
    /// <param name="traceReader">Opens the latest device-tagging trace.</param>
    /// <param name="operationLock">Detects whether an operation is already running.</param>
    /// <param name="requestQueue">Queues run requests for the queue-triggered workers.</param>
    /// <param name="timeProvider">Supplies the request timestamp of queued operations.</param>
    /// <param name="credentialInitializer">Validates and stores the initial Active Directory credential.</param>
    /// <param name="authorizer">Authorizes the Easy Auth principal of each request.</param>
    /// <param name="directoryConfigurationStore">Reads and updates the forest-domain configuration.</param>
    /// <param name="activityLog">The optional activity log.</param>
    public ManagementApiFunction(
        TaggingConfigurationService configurationService,
        IOperationStatusStore statusStore,
        IRunTraceReader traceReader,
        IOperationLock operationLock,
        IOperationRequestQueue requestQueue,
        TimeProvider timeProvider,
        IActiveDirectoryCredentialInitializer credentialInitializer,
        IManagementApiAuthorizer authorizer,
        IActiveDirectoryConfigurationStore directoryConfigurationStore,
        IActivityLog? activityLog = null)
    {
        _activityLog = activityLog ?? NullActivityLog.Instance;
        _configurationService = configurationService;
        _statusStore = statusStore;
        _traceReader = traceReader;
        _operationLock = operationLock;
        _requestQueue = requestQueue;
        _timeProvider = timeProvider;
        _credentialInitializer = credentialInitializer;
        _authorizer = authorizer;
        _directoryConfigurationStore = directoryConfigurationStore;
    }

    /// <summary>Gets the effective tagging-rule configuration.</summary>
    /// <remarks>Requires the Reader or RuleAdministrator role. The ETag is also returned as a response header.</remarks>
    /// <param name="request">The HTTP request carrying the Easy Auth principal.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 200 with the rules, ETag, last-modified time, and source; otherwise 401 or 403.</returns>
    [Function("GetTaggingConfiguration")]
    public async Task<IActionResult> GetTaggingConfigurationAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/management/tagging-rules")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (await AuthorizeAsync(
                request,
                cancellationToken,
                ManagementApiRoles.Reader,
                ManagementApiRoles.RuleAdministrator) is { } authorizationFailure)
        {
            return authorizationFailure;
        }

        return ConfigurationResult(
            request,
            await _configurationService.GetAsync(cancellationToken));
    }

    /// <summary>Replaces the complete tagging-rule configuration.</summary>
    /// <remarks>
    /// Requires the RuleAdministrator role. An optional <c>If-Match</c> header enables optimistic
    /// concurrency; without it the ETag read at the start of the request is used.
    /// </remarks>
    /// <param name="request">The HTTP request whose JSON body contains the complete rule list.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 200 with the stored configuration, or 400, 401, 403, 409, or 413.</returns>
    [Function("ReplaceTaggingConfiguration")]
    public async Task<IActionResult> ReplaceTaggingConfigurationAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "api/management/tagging-rules")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (await AuthorizeAsync(
                request,
                cancellationToken,
                ManagementApiRoles.RuleAdministrator) is { } authorizationFailure)
        {
            return authorizationFailure;
        }

        return RecordActivity(
            request,
            ActivityCategories.Configuration,
            "RulesReplaced",
            "all tagging rules",
            await ExecuteConfigurationMutationAsync(
                request,
                async () => await _configurationService.ReplaceAsync(
                    await ReadJsonAsync<List<TaggingRuleOptions>>(request, cancellationToken),
                    cancellationToken,
                    GetExpectedETag(request),
                    await IsFunctionAppAdministratorAsync(request, cancellationToken)),
                ConfigurationResult));
    }

    /// <summary>Adds or completely replaces one named tagging rule.</summary>
    /// <remarks>Requires the RuleAdministrator role and honors an optional <c>If-Match</c> header.</remarks>
    /// <param name="request">The HTTP request whose JSON body contains the rule.</param>
    /// <param name="ruleName">The route rule name, which must match the body name case-insensitively.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 200 with the stored configuration, or 400, 401, 403, 409, or 413.</returns>
    [Function("UpsertTaggingRule")]
    public async Task<IActionResult> UpsertTaggingRuleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "api/management/tagging-rules/{ruleName}")]
        HttpRequest request,
        string ruleName,
        CancellationToken cancellationToken)
    {
        if (await AuthorizeAsync(
                request,
                cancellationToken,
                ManagementApiRoles.RuleAdministrator) is { } authorizationFailure)
        {
            return authorizationFailure;
        }

        return RecordActivity(request, ActivityCategories.Configuration, "RuleUpserted", ruleName,
            await ExecuteConfigurationMutationAsync(request, async () =>
        {
            var rule = await ReadJsonAsync<TaggingRuleOptions>(request, cancellationToken);
            if (!string.Equals(ruleName, rule.Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new TaggingConfigurationValidationException(
                    ["The route ruleName must match the rule Name in the request body."]);
            }

            return await _configurationService.UpsertAsync(
                rule,
                cancellationToken,
                GetExpectedETag(request),
                await IsFunctionAppAdministratorAsync(request, cancellationToken));
        }, ConfigurationResult));
    }

    /// <summary>Removes one named tagging rule.</summary>
    /// <remarks>Requires the RuleAdministrator role and honors an optional <c>If-Match</c> header.</remarks>
    /// <param name="request">The HTTP request carrying the Easy Auth principal.</param>
    /// <param name="ruleName">The case-insensitive name of the rule to remove.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 200 with the stored configuration, or 400, 401, 403, 404, or 409.</returns>
    [Function("RemoveTaggingRule")]
    public async Task<IActionResult> RemoveTaggingRuleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "api/management/tagging-rules/{ruleName}")]
        HttpRequest request,
        string ruleName,
        CancellationToken cancellationToken)
    {
        if (await AuthorizeAsync(
                request,
                cancellationToken,
                ManagementApiRoles.RuleAdministrator) is { } authorizationFailure)
        {
            return authorizationFailure;
        }

        return RecordActivity(
            request,
            ActivityCategories.Configuration,
            "RuleRemoved",
            ruleName,
            await ExecuteConfigurationMutationAsync(
                request,
                () => _configurationService.RemoveAsync(
                    ruleName,
                    cancellationToken,
                    GetExpectedETag(request)),
                ConfigurationResult));
    }

    /// <summary>Gets the status of the latest device-tagging run.</summary>
    /// <remarks>Requires the Reader or TaggingOperator role.</remarks>
    /// <param name="request">The HTTP request carrying the Easy Auth principal.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 200 with the status, 404 when no run was recorded, or 401 or 403.</returns>
    [Function("GetDeviceTaggingStatus")]
    public Task<IActionResult> GetDeviceTaggingStatusAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/management/status/device-tagging")]
        HttpRequest request,
        CancellationToken cancellationToken) =>
        GetStatusAsync(
            request,
            OperationNames.DeviceTagging,
            cancellationToken,
            ManagementApiRoles.Reader,
            ManagementApiRoles.TaggingOperator);

    /// <summary>Gets the latest password-rotation check and actual password-change time.</summary>
    /// <remarks>Requires the Reader or PasswordRotationAdministrator role.</remarks>
    /// <param name="request">The HTTP request carrying the Easy Auth principal.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 200 with the status, 404 when no run was recorded, or 401 or 403.</returns>
    [Function("GetPasswordRotationStatus")]
    public Task<IActionResult> GetPasswordRotationStatusAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/management/status/password-rotation")]
        HttpRequest request,
        CancellationToken cancellationToken) =>
        GetStatusAsync(
            request,
            OperationNames.PasswordRotation,
            cancellationToken,
            ManagementApiRoles.Reader,
            ManagementApiRoles.PasswordRotationAdministrator);

    /// <summary>Queues an immediate device-tagging run.</summary>
    /// <remarks>Requires the TaggingOperator role.</remarks>
    /// <param name="request">The HTTP request carrying the Easy Auth principal.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 202 with the invocation ID and status URI, or 401, 403, or 409.</returns>
    [Function("RunDeviceTagging")]
    public async Task<IActionResult> RunDeviceTaggingAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/management/actions/device-tagging")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (await AuthorizeAsync(
                request,
                cancellationToken,
                ManagementApiRoles.TaggingOperator) is { } authorizationFailure)
        {
            return authorizationFailure;
        }

        return RecordActivity(
            request,
            ActivityCategories.Tagging,
            "TaggingRunRequested",
            null,
            await QueueOperationAsync(OperationNames.DeviceTagging, cancellationToken));
    }

    /// <summary>Queues an immediate Active Directory service-account password rotation.</summary>
    /// <remarks>Requires the PasswordRotationAdministrator role.</remarks>
    /// <param name="request">The HTTP request carrying the Easy Auth principal.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 202 with the invocation ID and status URI, or 401, 403, or 409.</returns>
    [Function("RunPasswordRotation")]
    public async Task<IActionResult> RunPasswordRotationAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/management/actions/password-rotation")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (await AuthorizeAsync(
                request,
                cancellationToken,
                ManagementApiRoles.PasswordRotationAdministrator) is { } authorizationFailure)
        {
            return authorizationFailure;
        }

        return RecordActivity(
            request,
            ActivityCategories.Password,
            "PasswordRotationRequested",
            null,
            await QueueOperationAsync(OperationNames.PasswordRotation, cancellationToken));
    }

    /// <summary>Validates and stores the initial Active Directory service-account password.</summary>
    /// <remarks>
    /// Requires the PasswordRotationAdministrator role. The password must contain 1 to 256
    /// characters, must bind to Active Directory, and is stored only when no credential exists yet.
    /// The password is never returned or logged.
    /// </remarks>
    /// <param name="request">The HTTP request whose JSON body contains the password.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 200 when initialized, 400 for invalid input or credentials, 409 when already initialized, 413, 401, or 403.</returns>
    [Function("InitializeActiveDirectoryCredential")]
    public async Task<IActionResult> InitializeActiveDirectoryCredentialAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/management/actions/initialize-ad-credential")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (await AuthorizeAsync(
                request,
                cancellationToken,
                ManagementApiRoles.PasswordRotationAdministrator) is { } authorizationFailure)
        {
            return authorizationFailure;
        }

        return RecordActivity(
            request,
            ActivityCategories.Password,
            "CredentialInitialized",
            null,
            await InitializeActiveDirectoryCredentialCoreAsync(request, cancellationToken));
    }

    /// <summary>Validates and stores the initial password and maps expected failures to HTTP results.</summary>
    /// <param name="request">The HTTP request whose JSON body contains the password.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 200, 400, 409, or 413.</returns>
    private async Task<IActionResult> InitializeActiveDirectoryCredentialCoreAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var input = await ReadJsonAsync<InitializeCredentialRequest>(request, cancellationToken);
            if (string.IsNullOrWhiteSpace(input.Password) || input.Password.Length > 256)
            {
                return new BadRequestObjectResult(new { error = "Password must contain between 1 and 256 characters." });
            }

            await _credentialInitializer.InitializeAsync(input.Password, cancellationToken);
            return new OkObjectResult(new { initialized = true });
        }
        catch (ActiveDirectoryCredentialValidationException exception)
        {
            return new BadRequestObjectResult(new
            {
                error = exception.Message,
                attempts = exception.Validation.Attempts
            });
        }
        catch (ActiveDirectoryCredentialAlreadyInitializedException exception)
        {
            return new ConflictObjectResult(new { error = exception.Message });
        }
        catch (ActiveDirectoryNotConfiguredException exception)
        {
            return new ConflictObjectResult(new { error = exception.Message, notConfigured = exception.MissingSetting });
        }
        catch (JsonException exception)
        {
            return new BadRequestObjectResult(new
            {
                error = "The request body is not valid JSON.",
                detail = exception.Message
            });
        }
        catch (RequestBodyTooLargeException)
        {
            return new ObjectResult(new { error = $"The request body cannot exceed {MaximumRequestBytes} bytes." })
            {
                StatusCode = StatusCodes.Status413PayloadTooLarge
            };
        }
    }

    /// <summary>Downloads the latest device-tagging JSONL trace.</summary>
    /// <remarks>Requires the Reader or TaggingOperator role.</remarks>
    /// <param name="request">The HTTP request carrying the Easy Auth principal.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The trace file stream, 404 when no trace exists, or 401 or 403.</returns>
    [Function("DownloadLatestDeviceTaggingTrace")]
    public async Task<IActionResult> DownloadLatestDeviceTaggingTraceAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/management/traces/device-tagging/latest")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (await AuthorizeAsync(
                request,
                cancellationToken,
                ManagementApiRoles.Reader,
                ManagementApiRoles.TaggingOperator) is { } authorizationFailure)
        {
            return authorizationFailure;
        }

        var trace = await _traceReader.OpenLatestAsync(cancellationToken);
        return trace is null
            ? new NotFoundObjectResult(new { error = "No device-tagging trace is available." })
            : new FileStreamResult(trace.Content, trace.ContentType)
            {
                FileDownloadName = trace.FileName
            };
    }

    /// <summary>Authorizes the caller and returns the latest status of one operation.</summary>
    /// <param name="request">The HTTP request carrying the Easy Auth principal.</param>
    /// <param name="operation">The operation whose status is read.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <param name="requiredRoles">The roles of which the caller needs at least one.</param>
    /// <returns>HTTP 200 with the status, 404 when no status exists, or 401 or 403.</returns>
    private async Task<IActionResult> GetStatusAsync(
        HttpRequest request,
        string operation,
        CancellationToken cancellationToken,
        params string[] requiredRoles)
    {
        if (await AuthorizeAsync(
                request,
                cancellationToken,
                requiredRoles) is { } authorizationFailure)
        {
            return authorizationFailure;
        }

        var status = await _statusStore.GetAsync(operation, cancellationToken);
        return status is null
            ? new NotFoundObjectResult(new { error = $"No {operation} status is available." })
            : new OkObjectResult(status);
    }

    /// <summary>Accepts a run request and leaves execution to the queue-triggered worker.</summary>
    /// <remarks>
    /// Runs can exceed the HTTP gateway timeout, so the caller polls the status endpoint with the
    /// returned invocation ID instead of waiting for the result. The lock check is a best-effort
    /// early rejection; the worker re-acquires the lock and records a rejection if it lost the race.
    /// When enqueueing fails, the queued status is marked failed and the exception is rethrown.
    /// </remarks>
    /// <param name="operation">The operation name used for the lock, status, and queue.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 202 with the invocation ID and status URI, or 409 when the operation is running.</returns>
    private async Task<IActionResult> QueueOperationAsync(
        string operation,
        CancellationToken cancellationToken)
    {
        if (await _operationLock.IsHeldAsync(operation, cancellationToken))
        {
            return new ConflictObjectResult(
                new { error = new OperationAlreadyRunningException(operation).Message });
        }

        var invocationId = Guid.NewGuid().ToString("D");
        await _statusStore.QueueAsync(operation, invocationId, cancellationToken);
        try
        {
            await _requestQueue.EnqueueAsync(
                operation,
                new OperationRequest(invocationId, _timeProvider.GetUtcNow()),
                cancellationToken);
        }
        catch (Exception exception)
        {
            await _statusStore.FailAsync(operation, invocationId, exception, CancellationToken.None);
            throw;
        }

        var statusUri = $"/api/management/status/{operation}";
        return new AcceptedResult(statusUri, new
        {
            invocationId,
            state = OperationRunState.Queued,
            statusUri
        });
    }

    /// <summary>Runs a configuration mutation and maps its expected failures to HTTP results.</summary>
    /// <param name="request">The HTTP request whose response receives the new ETag.</param>
    /// <param name="mutation">The mutation that reads the body and persists the configuration.</param>
    /// <param name="createResult">Creates the resource response including its ETag.</param>
    /// <typeparam name="T">The configuration snapshot type.</typeparam>
    /// <returns>
    /// HTTP 200 with the stored configuration; 400 for validation or JSON errors; 413 for an oversized
    /// body; 409 for an ETag conflict; or 404 for an unknown rule.
    /// </returns>
    private async Task<IActionResult> ExecuteConfigurationMutationAsync<T>(
        HttpRequest request,
        Func<Task<T>> mutation,
        Func<HttpRequest, T, IActionResult> createResult)
    {
        try
        {
            return createResult(request, await mutation());
        }
        catch (TaggingConfigurationValidationException exception)
        {
            return new BadRequestObjectResult(new
            {
                error = exception.Message,
                errors = exception.Errors
            });
        }
        catch (JsonException exception)
        {
            return new BadRequestObjectResult(new
            {
                error = "The request body is not valid JSON.",
                detail = exception.Message
            });
        }
        catch (ValidationException exception)
        {
            return new BadRequestObjectResult(new { error = exception.Message });
        }
        catch (RequestFailedException exception) when (exception.Status is 409 or 412)
        {
            return new ConflictObjectResult(new { error = "The configuration changed concurrently. Read it again before retrying." });
        }
        catch (RequestBodyTooLargeException)
        {
            return new ObjectResult(new { error = $"The request body cannot exceed {MaximumRequestBytes} bytes." })
            {
                StatusCode = StatusCodes.Status413PayloadTooLarge
            };
        }
        catch (TaggingConfigurationConflictException exception)
        {
            return new ConflictObjectResult(new { error = exception.Message });
        }
        catch (ActiveDirectoryNotConfiguredException exception)
        {
            return new ConflictObjectResult(new { error = exception.Message });
        }
        catch (KeyNotFoundException exception)
        {
            return new NotFoundObjectResult(new { error = exception.Message });
        }
    }

    /// <summary>Creates the configuration response and sets the ETag response header when known.</summary>
    /// <param name="request">The HTTP request whose response receives the ETag header.</param>
    /// <param name="snapshot">The configuration snapshot to return.</param>
    /// <returns>HTTP 200 with the rules, ETag, last-modified time, and source.</returns>
    private static IActionResult ConfigurationResult(
        HttpRequest request,
        TaggingConfigurationSnapshot snapshot)
    {
        if (snapshot.ETag is not null)
        {
            request.HttpContext.Response.Headers.ETag = snapshot.ETag;
        }

        return new OkObjectResult(new
        {
            snapshot.Rules,
            snapshot.ETag,
            snapshot.LastModifiedUtc,
            snapshot.Source
        });
    }

    /// <summary>Reads and deserializes a size-limited JSON request body.</summary>
    /// <remarks>
    /// The body is buffered in chunks and rejected as soon as it exceeds
    /// <see cref="MaximumRequestBytes"/>, so a missing or false <c>Content-Length</c> cannot
    /// force unbounded memory use.
    /// </remarks>
    /// <typeparam name="T">The request body type.</typeparam>
    /// <param name="request">The HTTP request whose body is read.</param>
    /// <param name="cancellationToken">Cancels reading the body.</param>
    /// <returns>The deserialized request body.</returns>
    /// <exception cref="RequestBodyTooLargeException">The body exceeds <see cref="MaximumRequestBytes"/>.</exception>
    /// <exception cref="JsonException">The body is not valid JSON, is empty, or is the JSON literal null.</exception>
    internal static async Task<T> ReadJsonAsync<T>(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaximumRequestBytes)
        {
            throw new RequestBodyTooLargeException();
        }

        await using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await request.Body.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (buffer.Length + read > MaximumRequestBytes)
            {
                throw new RequestBodyTooLargeException();
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        buffer.Position = 0;
        return await JsonSerializer.DeserializeAsync<T>(
                   buffer,
                   SerializerOptions,
                   cancellationToken) ??
               throw new JsonException("The request body cannot be empty or null.");
    }

    /// <summary>Creates the HTTP 401 result for a missing or invalid Entra principal.</summary>
    /// <returns>An HTTP 401 result with an error message.</returns>
    private static UnauthorizedObjectResult Unauthorized() =>
        new(new { error = "Microsoft Entra authentication is required." });

    /// <summary>Creates the HTTP 403 result for a principal without a required role.</summary>
    /// <returns>An HTTP 403 result with an error message.</returns>
    private static ObjectResult Forbidden() =>
        new(new { error = "The authenticated principal lacks the required management API role." })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };

    /// <summary>Authorizes the request principal for an endpoint.</summary>
    /// <param name="request">The HTTP request carrying the Easy Auth principal.</param>
    /// <param name="cancellationToken">Cancels a required Azure RBAC lookup.</param>
    /// <param name="requiredRoles">The roles of which the caller needs at least one.</param>
    /// <returns><see langword="null"/> when authorized; otherwise the HTTP 401 or 403 result to return.</returns>
    private async Task<IActionResult?> AuthorizeAsync(
        HttpRequest request,
        CancellationToken cancellationToken,
        params string[] requiredRoles)
    {
        var result = await _authorizer.AuthorizeAsync(request, requiredRoles, cancellationToken);
        if (result == EasyAuthAuthorizationResult.Authorized)
        {
            return null;
        }

        _activityLog.Record(new ActivityEntry(
            ActivityCategories.Access,
            "RequestDenied",
            ActivityOutcomes.Denied,
            EasyAuthPrincipalValidator.GetActor(request),
            $"{request.Method} {request.Path}",
            $"Required one of: {string.Join(", ", requiredRoles)}"));
        return result == EasyAuthAuthorizationResult.Forbidden ? Forbidden() : Unauthorized();
    }

    /// <summary>Returns whether the caller is an effective Function App Owner or Contributor.</summary>
    /// <param name="request">The HTTP request carrying the Easy Auth principal.</param>
    /// <param name="cancellationToken">Cancels the Azure RBAC lookup.</param>
    /// <returns><see langword="true"/> for Owners and Contributors.</returns>
    private async Task<bool> IsFunctionAppAdministratorAsync(HttpRequest request, CancellationToken cancellationToken) =>
        await _authorizer.AuthorizeFunctionAppAdministratorAsync(request, cancellationToken) ==
        EasyAuthAuthorizationResult.Authorized;

    /// <summary>Authorizes only effective Function App Owners and Contributors and records denials.</summary>
    /// <param name="request">The HTTP request carrying the Easy Auth principal.</param>
    /// <param name="cancellationToken">Cancels the Azure RBAC lookup.</param>
    /// <returns><see langword="null"/> when authorized; otherwise the HTTP 401 or 403 result to return.</returns>
    private async Task<IActionResult?> AuthorizeFunctionAppAdministratorAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authorizer.AuthorizeFunctionAppAdministratorAsync(request, cancellationToken);
        if (result == EasyAuthAuthorizationResult.Authorized)
        {
            return null;
        }

        _activityLog.Record(new ActivityEntry(
            ActivityCategories.Access,
            "RequestDenied",
            ActivityOutcomes.Denied,
            EasyAuthPrincipalValidator.GetActor(request),
            $"{request.Method} {request.Path}",
            "Requires Owner or Contributor on the Function App."));
        return result == EasyAuthAuthorizationResult.Forbidden
            ? new ObjectResult(new { error = "This operation requires the Owner or Contributor role on the Function App." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            }
            : Unauthorized();
    }

    /// <summary>Records the outcome of a management change and returns its result unchanged.</summary>
    /// <param name="request">The request identifying the actor.</param>
    /// <param name="category">The activity category.</param>
    /// <param name="action">The action name.</param>
    /// <param name="target">The affected object.</param>
    /// <param name="result">The HTTP result of the change.</param>
    /// <returns><paramref name="result"/>.</returns>
    private IActionResult RecordActivity(
        HttpRequest request,
        string category,
        string action,
        string? target,
        IActionResult result)
    {
        var statusCode = result switch
        {
            IStatusCodeActionResult { StatusCode: { } code } => code,
            _ => StatusCodes.Status200OK
        };
        _activityLog.Record(new ActivityEntry(
            category,
            action,
            statusCode < 400 ? ActivityOutcomes.Succeeded : ActivityOutcomes.Failed,
            EasyAuthPrincipalValidator.GetActor(request),
            target,
            statusCode < 400 ? null : $"HTTP {statusCode}: {GetError(result)}"));
        return result;
    }

    /// <summary>Extracts the error message of an error result body.</summary>
    /// <param name="result">The HTTP result.</param>
    /// <returns>The error text, or an empty string.</returns>
    private static string GetError(IActionResult result)
    {
        if (result is not ObjectResult { Value: { } value })
        {
            return string.Empty;
        }

        using var document = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), SerializerOptions));
        var parts = new List<string>();
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                parts.Add(error.ToString());
            }
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                parts.Add(errors.ToString());
            }
        }
        return string.Join(" ", parts);
    }

    /// <summary>Reads the optional optimistic-concurrency ETag from the <c>If-Match</c> header.</summary>
    /// <param name="request">The HTTP request whose headers are read.</param>
    /// <returns>The single <c>If-Match</c> value, or <see langword="null"/> when the header is absent.</returns>
    /// <exception cref="TaggingConfigurationValidationException">The header has several or an empty value.</exception>
    internal static string? GetExpectedETag(HttpRequest request)
    {
        var values = request.Headers.IfMatch;
        if (values.Count > 1 || values.Count == 1 && string.IsNullOrWhiteSpace(values[0]))
        {
            throw new TaggingConfigurationValidationException(
                ["If-Match must contain exactly one non-empty ETag."]);
        }

        return values.Count == 1 ? values[0] : null;
    }

    /// <summary>Signals that a request body exceeds <see cref="MaximumRequestBytes"/>.</summary>
    internal sealed class RequestBodyTooLargeException : Exception;

    /// <summary>Represents the body of the credential-initialization request.</summary>
    /// <param name="Password">The initial Active Directory service-account password.</param>
    private sealed record InitializeCredentialRequest(string Password);
}