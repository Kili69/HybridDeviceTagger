/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using System.Text.Json;
using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using HybridDeviceTagger.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace HybridDeviceTagger;

/// <summary>Provides the Entra-authenticated operational management API.</summary>
public sealed class ManagementApiFunction
{
    /// <summary>
    /// The maximum accepted request-body size in bytes (64 KiB), enforced while streaming so oversized
    /// bodies are rejected before they are fully buffered.
    /// </summary>
    private const int MaximumRequestBytes = 65536;

    /// <summary>Web-default JSON options with case-insensitive property matching for request bodies.</summary>
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    /// <summary>Reads and mutates the persisted tagging-rule configuration.</summary>
    private readonly TaggingConfigurationService _configurationService;

    /// <summary>Reads and records latest-run status for each operation.</summary>
    private readonly IOperationStatusStore _statusStore;

    /// <summary>Opens the latest device-tagging trace for download.</summary>
    private readonly IRunTraceReader _traceReader;

    /// <summary>Detects operations that are already running before a new request is queued.</summary>
    private readonly IOperationLock _operationLock;

    /// <summary>Queues run requests for the queue-triggered workers.</summary>
    private readonly IOperationRequestQueue _requestQueue;

    /// <summary>Supplies the request timestamp written into queued run requests.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the management API.</summary>
    /// <param name="configurationService">The tagging-rule configuration service.</param>
    /// <param name="statusStore">The latest-run status store.</param>
    /// <param name="traceReader">The latest device-tagging trace reader.</param>
    /// <param name="operationLock">The cross-instance operation lock.</param>
    /// <param name="requestQueue">The queue that dispatches requested runs.</param>
    /// <param name="timeProvider">The clock used to timestamp run requests.</param>
    public ManagementApiFunction(
        TaggingConfigurationService configurationService,
        IOperationStatusStore statusStore,
        IRunTraceReader traceReader,
        IOperationLock operationLock,
        IOperationRequestQueue requestQueue,
        TimeProvider timeProvider)
    {
        _configurationService = configurationService;
        _statusStore = statusStore;
        _traceReader = traceReader;
        _operationLock = operationLock;
        _requestQueue = requestQueue;
        _timeProvider = timeProvider;
    }

    /// <summary>Gets the effective tagging-rule configuration.</summary>
    /// <remarks>Requires the Reader or RuleAdministrator role. The response sets the <c>ETag</c> header.</remarks>
    /// <param name="request">The incoming HTTP request carrying the Easy Auth principal.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 200 with the rules, ETag, last-modified time, and source; or 401/403.</returns>
    [Function("GetTaggingConfiguration")]
    public async Task<IActionResult> GetTaggingConfigurationAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "management/tagging-rules")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (Authorize(request, ManagementApiRoles.Reader, ManagementApiRoles.RuleAdministrator) is { } authorizationFailure)
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
    /// concurrency so concurrent administrators cannot silently overwrite each other's changes.
    /// </remarks>
    /// <param name="request">The incoming HTTP request carrying the Easy Auth principal.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 200 with the new configuration; or 400, 401, 403, 409, or 413.</returns>
    [Function("ReplaceTaggingConfiguration")]
    public async Task<IActionResult> ReplaceTaggingConfigurationAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "management/tagging-rules")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (Authorize(request, ManagementApiRoles.RuleAdministrator) is { } authorizationFailure)
        {
            return authorizationFailure;
        }

        return await ExecuteConfigurationMutationAsync(
            request,
            async () => await _configurationService.ReplaceAsync(
                await ReadJsonAsync<List<TaggingRuleOptions>>(request, cancellationToken),
                cancellationToken,
                GetExpectedETag(request)));
    }

    /// <summary>Adds or completely replaces one named tagging rule.</summary>
    /// <remarks>
    /// Requires the RuleAdministrator role. The route name must match the body's rule name
    /// (case-insensitive); an optional <c>If-Match</c> header enables optimistic concurrency.
    /// </remarks>
    /// <param name="request">The incoming HTTP request carrying the Easy Auth principal.</param>
    /// <param name="ruleName">The rule name from the route.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 200 with the new configuration; or 400, 401, 403, 409, or 413.</returns>
    [Function("UpsertTaggingRule")]
    public async Task<IActionResult> UpsertTaggingRuleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "management/tagging-rules/{ruleName}")]
        HttpRequest request,
        string ruleName,
        CancellationToken cancellationToken)
    {
        if (Authorize(request, ManagementApiRoles.RuleAdministrator) is { } authorizationFailure)
        {
            return authorizationFailure;
        }

        return await ExecuteConfigurationMutationAsync(request, async () =>
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
                GetExpectedETag(request));
        });
    }

    /// <summary>Removes one named tagging rule.</summary>
    /// <remarks>Requires the RuleAdministrator role and honors an optional <c>If-Match</c> header.</remarks>
    /// <param name="request">The incoming HTTP request carrying the Easy Auth principal.</param>
    /// <param name="ruleName">The name of the rule to remove.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 200 with the new configuration; or 400, 401, 403, 404, or 409.</returns>
    [Function("RemoveTaggingRule")]
    public async Task<IActionResult> RemoveTaggingRuleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "management/tagging-rules/{ruleName}")]
        HttpRequest request,
        string ruleName,
        CancellationToken cancellationToken)
    {
        if (Authorize(request, ManagementApiRoles.RuleAdministrator) is { } authorizationFailure)
        {
            return authorizationFailure;
        }

        return await ExecuteConfigurationMutationAsync(
            request,
            () => _configurationService.RemoveAsync(
                ruleName,
                cancellationToken,
                GetExpectedETag(request)));
    }

    /// <summary>Gets the status of the latest device-tagging run.</summary>
    /// <remarks>Requires the Reader or TaggingOperator role.</remarks>
    /// <param name="request">The incoming HTTP request carrying the Easy Auth principal.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 200 with the status; 404 when no run was recorded; or 401/403.</returns>
    [Function("GetDeviceTaggingStatus")]
    public Task<IActionResult> GetDeviceTaggingStatusAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "management/status/device-tagging")]
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
    /// <param name="request">The incoming HTTP request carrying the Easy Auth principal.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 200 with the status; 404 when no run was recorded; or 401/403.</returns>
    [Function("GetPasswordRotationStatus")]
    public Task<IActionResult> GetPasswordRotationStatusAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "management/status/password-rotation")]
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
    /// <param name="request">The incoming HTTP request carrying the Easy Auth principal.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 202 with the invocation ID and status URI; 409 when a run is active; or 401/403.</returns>
    [Function("RunDeviceTagging")]
    public async Task<IActionResult> RunDeviceTaggingAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "management/actions/device-tagging")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (Authorize(request, ManagementApiRoles.TaggingOperator) is { } authorizationFailure)
        {
            return authorizationFailure;
        }

        return await QueueOperationAsync(OperationNames.DeviceTagging, cancellationToken);
    }

    /// <summary>Queues an immediate Active Directory service-account password rotation.</summary>
    /// <remarks>
    /// Requires the PasswordRotationAdministrator role. The queued run rotates regardless of the
    /// current password age.
    /// </remarks>
    /// <param name="request">The incoming HTTP request carrying the Easy Auth principal.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>HTTP 202 with the invocation ID and status URI; 409 when a run is active; or 401/403.</returns>
    [Function("RunPasswordRotation")]
    public async Task<IActionResult> RunPasswordRotationAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "management/actions/password-rotation")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (Authorize(request, ManagementApiRoles.PasswordRotationAdministrator) is { } authorizationFailure)
        {
            return authorizationFailure;
        }

        return await QueueOperationAsync(OperationNames.PasswordRotation, cancellationToken);
    }

    /// <summary>Downloads the latest device-tagging JSONL trace.</summary>
    /// <remarks>Requires the Reader or TaggingOperator role.</remarks>
    /// <param name="request">The incoming HTTP request carrying the Easy Auth principal.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The trace as a file download; 404 when no trace exists; or 401/403.</returns>
    [Function("DownloadLatestDeviceTaggingTrace")]
    public async Task<IActionResult> DownloadLatestDeviceTaggingTraceAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "management/traces/device-tagging/latest")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (Authorize(request, ManagementApiRoles.Reader, ManagementApiRoles.TaggingOperator) is { } authorizationFailure)
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
    /// <param name="request">The incoming HTTP request carrying the Easy Auth principal.</param>
    /// <param name="operation">The operation name, for example <see cref="OperationNames.DeviceTagging"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <param name="requiredRoles">Roles of which the caller must hold at least one.</param>
    /// <returns>HTTP 200 with the status; 404 when no status exists; or 401/403.</returns>
    private async Task<IActionResult> GetStatusAsync(
        HttpRequest request,
        string operation,
        CancellationToken cancellationToken,
        params string[] requiredRoles)
    {
        if (Authorize(request, requiredRoles) is { } authorizationFailure)
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

    /// <summary>Runs a configuration mutation and maps expected failures to HTTP error results.</summary>
    /// <remarks>
    /// Validation and malformed JSON map to 400, oversized bodies to 413, ETag conflicts to 409, and
    /// unknown rules to 404. Other exceptions propagate to the Functions host.
    /// </remarks>
    /// <param name="request">The request whose response receives the new <c>ETag</c> header.</param>
    /// <param name="mutation">The mutation that reads the request body and persists the change.</param>
    /// <returns>HTTP 200 with the new configuration, or the mapped error result.</returns>
    private async Task<IActionResult> ExecuteConfigurationMutationAsync(
        HttpRequest request,
        Func<Task<TaggingConfigurationSnapshot>> mutation)
    {
        try
        {
            return ConfigurationResult(request, await mutation());
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
        catch (KeyNotFoundException exception)
        {
            return new NotFoundObjectResult(new { error = exception.Message });
        }
    }

    /// <summary>Creates the HTTP 200 configuration response and sets the <c>ETag</c> header when known.</summary>
    /// <param name="request">The request whose response headers are updated.</param>
    /// <param name="snapshot">The configuration snapshot to return.</param>
    /// <returns>An HTTP 200 result with rules, ETag, last-modified time, and source.</returns>
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
    /// The body is read in chunks and the limit is checked both against <c>Content-Length</c> and the
    /// bytes actually received, so chunked requests cannot bypass it.
    /// </remarks>
    /// <typeparam name="T">The type to deserialize.</typeparam>
    /// <param name="request">The request whose body is read.</param>
    /// <param name="cancellationToken">Cancels reading the body.</param>
    /// <returns>The deserialized body.</returns>
    /// <exception cref="RequestBodyTooLargeException">The body exceeds <see cref="MaximumRequestBytes"/>.</exception>
    /// <exception cref="JsonException">The body is not valid JSON, is empty, or is <c>null</c>.</exception>
    private static async Task<T> ReadJsonAsync<T>(
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

    /// <summary>Creates the HTTP 401 result returned when no valid Entra principal is present.</summary>
    /// <returns>An HTTP 401 result with a generic error message.</returns>
    private static UnauthorizedObjectResult Unauthorized() =>
        new(new { error = "Microsoft Entra authentication is required." });

    /// <summary>Creates the HTTP 403 result returned when the principal lacks every required role.</summary>
    /// <returns>An HTTP 403 result with a generic error message.</returns>
    private static ObjectResult Forbidden() =>
        new(new { error = "The authenticated principal lacks the required management API role." })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };

    /// <summary>Validates the Easy Auth principal and its app roles.</summary>
    /// <param name="request">The request carrying the Easy Auth principal header.</param>
    /// <param name="requiredRoles">Roles of which the caller must hold at least one.</param>
    /// <returns>
    /// <see langword="null"/> when authorized; otherwise the HTTP 401 or 403 result to return.
    /// </returns>
    private static IActionResult? Authorize(HttpRequest request, params string[] requiredRoles) =>
        EasyAuthPrincipalValidator.Authorize(request, requiredRoles) switch
        {
            EasyAuthAuthorizationResult.Authorized => null,
            EasyAuthAuthorizationResult.Forbidden => Forbidden(),
            _ => Unauthorized()
        };

    /// <summary>Reads the optional <c>If-Match</c> header used for optimistic concurrency.</summary>
    /// <param name="request">The request whose headers are read.</param>
    /// <returns>The expected ETag, or <see langword="null"/> when the header is absent.</returns>
    /// <exception cref="TaggingConfigurationValidationException">
    /// The header occurs more than once or is empty.
    /// </exception>
    private static string? GetExpectedETag(HttpRequest request)
    {
        var values = request.Headers.IfMatch;
        if (values.Count > 1 || values.Count == 1 && string.IsNullOrWhiteSpace(values[0]))
        {
            throw new TaggingConfigurationValidationException(
                ["If-Match must contain exactly one non-empty ETag."]);
        }

        return values.Count == 1 ? values[0] : null;
    }

    /// <summary>Signals that a request body exceeded <see cref="MaximumRequestBytes"/>; mapped to HTTP 413.</summary>
    private sealed class RequestBodyTooLargeException : Exception;
}