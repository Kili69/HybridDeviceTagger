/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>
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
    private const int MaximumRequestBytes = 65536;
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    private readonly TaggingConfigurationService _configurationService;
    private readonly IOperationStatusStore _statusStore;
    private readonly IRunTraceReader _traceReader;
    private readonly IDeviceTaggingRunner _deviceTaggingRunner;
    private readonly IPasswordRotationRunner _passwordRotationRunner;

    /// <summary>Initializes the management API.</summary>
    public ManagementApiFunction(
        TaggingConfigurationService configurationService,
        IOperationStatusStore statusStore,
        IRunTraceReader traceReader,
        IDeviceTaggingRunner deviceTaggingRunner,
        IPasswordRotationRunner passwordRotationRunner)
    {
        _configurationService = configurationService;
        _statusStore = statusStore;
        _traceReader = traceReader;
        _deviceTaggingRunner = deviceTaggingRunner;
        _passwordRotationRunner = passwordRotationRunner;
    }

    /// <summary>Gets the effective tagging-rule configuration.</summary>
    [Function("GetTaggingConfiguration")]
    public async Task<IActionResult> GetTaggingConfigurationAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "management/tagging-rules")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!EasyAuthPrincipalValidator.IsAuthenticated(request))
        {
            return Unauthorized();
        }

        return ConfigurationResult(
            request,
            await _configurationService.GetAsync(cancellationToken));
    }

    /// <summary>Replaces the complete tagging-rule configuration.</summary>
    [Function("ReplaceTaggingConfiguration")]
    public async Task<IActionResult> ReplaceTaggingConfigurationAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "management/tagging-rules")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!EasyAuthPrincipalValidator.IsAuthenticated(request))
        {
            return Unauthorized();
        }

        return await ExecuteConfigurationMutationAsync(
            request,
            async () => await _configurationService.ReplaceAsync(
                await ReadJsonAsync<List<TaggingRuleOptions>>(request, cancellationToken),
                cancellationToken,
                GetExpectedETag(request)));
    }

    /// <summary>Adds or completely replaces one named tagging rule.</summary>
    [Function("UpsertTaggingRule")]
    public async Task<IActionResult> UpsertTaggingRuleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "management/tagging-rules/{ruleName}")]
        HttpRequest request,
        string ruleName,
        CancellationToken cancellationToken)
    {
        if (!EasyAuthPrincipalValidator.IsAuthenticated(request))
        {
            return Unauthorized();
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
    [Function("RemoveTaggingRule")]
    public async Task<IActionResult> RemoveTaggingRuleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "management/tagging-rules/{ruleName}")]
        HttpRequest request,
        string ruleName,
        CancellationToken cancellationToken)
    {
        if (!EasyAuthPrincipalValidator.IsAuthenticated(request))
        {
            return Unauthorized();
        }

        return await ExecuteConfigurationMutationAsync(
            request,
            () => _configurationService.RemoveAsync(
                ruleName,
                cancellationToken,
                GetExpectedETag(request)));
    }

    /// <summary>Gets the status of the latest device-tagging run.</summary>
    [Function("GetDeviceTaggingStatus")]
    public Task<IActionResult> GetDeviceTaggingStatusAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "management/status/device-tagging")]
        HttpRequest request,
        CancellationToken cancellationToken) =>
        GetStatusAsync(request, OperationNames.DeviceTagging, cancellationToken);

    /// <summary>Gets the latest password-rotation check and actual password-change time.</summary>
    [Function("GetPasswordRotationStatus")]
    public Task<IActionResult> GetPasswordRotationStatusAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "management/status/password-rotation")]
        HttpRequest request,
        CancellationToken cancellationToken) =>
        GetStatusAsync(request, OperationNames.PasswordRotation, cancellationToken);

    /// <summary>Immediately runs device tagging.</summary>
    [Function("RunDeviceTagging")]
    public async Task<IActionResult> RunDeviceTaggingAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "management/actions/device-tagging")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!EasyAuthPrincipalValidator.IsAuthenticated(request))
        {
            return Unauthorized();
        }

        var invocationId = Guid.NewGuid().ToString("D");
        return await ExecuteOperationAsync(
            invocationId,
            () => _deviceTaggingRunner.RunAsync(invocationId, false, cancellationToken));
    }

    /// <summary>Immediately rotates the Active Directory service-account password.</summary>
    [Function("RunPasswordRotation")]
    public async Task<IActionResult> RunPasswordRotationAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "management/actions/password-rotation")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!EasyAuthPrincipalValidator.IsAuthenticated(request))
        {
            return Unauthorized();
        }

        var invocationId = Guid.NewGuid().ToString("D");
        return await ExecuteOperationAsync(
            invocationId,
            () => _passwordRotationRunner.RunAsync(invocationId, false, true, cancellationToken));
    }

    /// <summary>Downloads the latest device-tagging JSONL trace.</summary>
    [Function("DownloadLatestDeviceTaggingTrace")]
    public async Task<IActionResult> DownloadLatestDeviceTaggingTraceAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "management/traces/device-tagging/latest")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!EasyAuthPrincipalValidator.IsAuthenticated(request))
        {
            return Unauthorized();
        }

        var trace = await _traceReader.OpenLatestAsync(cancellationToken);
        return trace is null
            ? new NotFoundObjectResult(new { error = "No device-tagging trace is available." })
            : new FileStreamResult(trace.Content, trace.ContentType)
            {
                FileDownloadName = trace.FileName
            };
    }

    private async Task<IActionResult> GetStatusAsync(
        HttpRequest request,
        string operation,
        CancellationToken cancellationToken)
    {
        if (!EasyAuthPrincipalValidator.IsAuthenticated(request))
        {
            return Unauthorized();
        }

        var status = await _statusStore.GetAsync(operation, cancellationToken);
        return status is null
            ? new NotFoundObjectResult(new { error = $"No {operation} status is available." })
            : new OkObjectResult(status);
    }

    private static async Task<IActionResult> ExecuteOperationAsync<T>(
        string invocationId,
        Func<Task<T>> operation)
    {
        try
        {
            return new OkObjectResult(new
            {
                invocationId,
                result = await operation()
            });
        }
        catch (OperationAlreadyRunningException exception)
        {
            return new ConflictObjectResult(new { error = exception.Message });
        }
    }

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

    private static UnauthorizedObjectResult Unauthorized() =>
        new(new { error = "Microsoft Entra authentication is required." });

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

    private sealed class RequestBodyTooLargeException : Exception;
}