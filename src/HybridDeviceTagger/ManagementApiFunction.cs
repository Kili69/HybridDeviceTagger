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
    private const int MaximumRequestBytes = 65536;
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    private readonly TaggingConfigurationService _configurationService;
    private readonly IOperationStatusStore _statusStore;
    private readonly IRunTraceReader _traceReader;
    private readonly IActiveDirectoryTrustStore _trustStore;
    private readonly IDeviceTaggingRunner _deviceTaggingRunner;
    private readonly IPasswordRotationRunner _passwordRotationRunner;

    /// <summary>Initializes the management API.</summary>
    public ManagementApiFunction(
        TaggingConfigurationService configurationService,
        IOperationStatusStore statusStore,
        IRunTraceReader traceReader,
        IActiveDirectoryTrustStore trustStore,
        IDeviceTaggingRunner deviceTaggingRunner,
        IPasswordRotationRunner passwordRotationRunner)
    {
        _configurationService = configurationService;
        _statusStore = statusStore;
        _traceReader = traceReader;
        _trustStore = trustStore;
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
        if (Authorize(request, ManagementApiRoles.Reader, ManagementApiRoles.RuleAdministrator) is { } authorizationFailure)
        {
            return authorizationFailure;
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

    /// <summary>Immediately runs device tagging.</summary>
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
        if (Authorize(request, ManagementApiRoles.PasswordRotationAdministrator) is { } authorizationFailure)
        {
            return authorizationFailure;
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

    /// <summary>Gets metadata for the effective Active Directory LDAPS trust certificates.</summary>
    [Function("GetTrustedCertificates")]
    public async Task<IActionResult> GetTrustedCertificatesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "management/trusted-certificates")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (Authorize(
            request,
            ManagementApiRoles.Reader,
            ManagementApiRoles.CertificateAdministrator) is { } authorizationFailure)
        {
            return authorizationFailure;
        }

        return TrustedCertificateResult(
            request,
            await _trustStore.GetSnapshotAsync(cancellationToken));
    }

    /// <summary>Adds one public Active Directory LDAPS trust certificate.</summary>
    [Function("AddTrustedCertificate")]
    public async Task<IActionResult> AddTrustedCertificateAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "management/trusted-certificates")]
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (Authorize(request, ManagementApiRoles.CertificateAdministrator) is { } authorizationFailure)
        {
            return authorizationFailure;
        }

        return await ExecuteTrustedCertificateMutationAsync(request, async () =>
        {
            var input = await ReadJsonAsync<AddTrustedCertificateRequest>(request, cancellationToken);
            if (string.IsNullOrWhiteSpace(input.Certificate))
            {
                throw new TrustedCertificateValidationException(
                    "Certificate must contain base64-encoded DER or PEM data.");
            }
            byte[] certificateBytes;
            try
            {
                certificateBytes = Convert.FromBase64String(input.Certificate);
            }
            catch (FormatException exception)
            {
                throw new TrustedCertificateValidationException(
                    "Certificate must be a base64-encoded DER or PEM X.509 certificate.",
                    exception);
            }

            return await _trustStore.AddAsync(
                certificateBytes,
                GetExpectedETag(request),
                cancellationToken);
        });
    }

    /// <summary>Removes one Active Directory LDAPS trust certificate.</summary>
    [Function("RemoveTrustedCertificate")]
    public async Task<IActionResult> RemoveTrustedCertificateAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "management/trusted-certificates/{thumbprint}")]
        HttpRequest request,
        string thumbprint,
        CancellationToken cancellationToken)
    {
        if (Authorize(request, ManagementApiRoles.CertificateAdministrator) is { } authorizationFailure)
        {
            return authorizationFailure;
        }

        return await ExecuteTrustedCertificateMutationAsync(
            request,
            () => _trustStore.RemoveAsync(
                thumbprint,
                GetExpectedETag(request),
                cancellationToken));
    }

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

    private async Task<IActionResult> ExecuteTrustedCertificateMutationAsync(
        HttpRequest request,
        Func<Task<TrustedCertificateSnapshot>> mutation)
    {
        try
        {
            return TrustedCertificateResult(request, await mutation());
        }
        catch (TrustedCertificateValidationException exception)
        {
            return new BadRequestObjectResult(new { error = exception.Message });
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
        catch (TrustedCertificateConflictException exception)
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

    private static IActionResult TrustedCertificateResult(
        HttpRequest request,
        TrustedCertificateSnapshot snapshot)
    {
        if (snapshot.ETag is not null)
        {
            request.HttpContext.Response.Headers.ETag = snapshot.ETag;
        }

        return new OkObjectResult(new
        {
            snapshot.Certificates,
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

    private static ObjectResult Forbidden() =>
        new(new { error = "The authenticated principal lacks the required management API role." })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };

    private static IActionResult? Authorize(HttpRequest request, params string[] requiredRoles) =>
        EasyAuthPrincipalValidator.Authorize(request, requiredRoles) switch
        {
            EasyAuthAuthorizationResult.Authorized => null,
            EasyAuthAuthorizationResult.Forbidden => Forbidden(),
            _ => Unauthorized()
        };

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

    private sealed record AddTrustedCertificateRequest(string Certificate);
}