using System.Text;
using System.Text.Json;
using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using HybridDeviceTagger.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HybridDeviceTagger.Tests;

public sealed class ManagementApiFunctionTests
{
    [Fact]
    public void GetAuthentication_ReturnsConfiguredTokenScopeWithoutPrincipal()
    {
        var function = new ManagementApiAuthenticationFunction(
            Microsoft.Extensions.Options.Options.Create(new ManagementApiOptions
            {
                ClientId = "11111111-1111-1111-1111-111111111111"
            }));

        var result = function.GetAuthentication(new DefaultHttpContext().Request);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(
            "api://11111111-1111-1111-1111-111111111111/.default",
            ok.Value?.GetType().GetProperty("tokenScope")?.GetValue(ok.Value));
    }

    [Fact]
    public void OperationRunStatus_SerializesStateAsReadableName()
    {
        var status = new OperationRunStatus(
            OperationNames.DeviceTagging,
            "invocation",
            OperationRunState.Succeeded,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            null);

        var json = JsonSerializer.Serialize(status, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"state\":\"Succeeded\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTaggingConfigurationAsync_RejectsMissingEasyAuthPrincipal()
    {
        var function = CreateFunction(new FakeTaggingConfigurationStore(), new FakeStatusStore(), new FakeTraceReader());

        var result = await function.GetTaggingConfigurationAsync(
            new DefaultHttpContext().Request,
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task ReplaceTaggingConfigurationAsync_ForwardsIfMatch()
    {
        var configurationStore = new FakeTaggingConfigurationStore();
        var function = CreateFunction(configurationStore, new FakeStatusStore(), new FakeTraceReader());
        var request = AuthenticatedRequest(ManagementApiRoles.RuleAdministrator);
        request.Headers.IfMatch = "client-etag";
        request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
            """[{"name":"Servers","description":"Test","baseDn":"OU=Servers,DC=example,DC=com","extensionAttributes":{"extensionAttribute1":"Managed"},"entraGroupIds":[],"administrativeUnitIds":[]}]"""));

        var result = await function.ReplaceTaggingConfigurationAsync(
            request,
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("client-etag", configurationStore.LastExpectedETag);
    }

    [Fact]
    public async Task ReplaceTaggingConfigurationAsync_RejectsDomainOutsideForest()
    {
        var configurationStore = new FakeTaggingConfigurationStore();
        var function = CreateFunction(
            configurationStore,
            new FakeStatusStore(),
            new FakeTraceReader(),
            ruleValidator: new RejectingActiveDirectoryRuleValidator());
        var request = AuthenticatedRequest(ManagementApiRoles.RuleAdministrator);
        request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
            """[{"name":"External","description":"Blocked","baseDn":"OU=Servers,DC=attacker,DC=example","extensionAttributes":{"extensionAttribute1":"Managed"},"entraGroupIds":[],"administrativeUnitIds":[]}]"""));

        var result = await function.ReplaceTaggingConfigurationAsync(
            request,
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("invalid", badRequest.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Null(configurationStore.LastExpectedETag);
    }

    [Fact]
    public async Task GetDeviceTaggingStatusAsync_ReturnsNotFoundBeforeFirstRun()
    {
        var function = CreateFunction(new FakeTaggingConfigurationStore(), new FakeStatusStore(), new FakeTraceReader());

        var result = await function.GetDeviceTaggingStatusAsync(
            AuthenticatedRequest(ManagementApiRoles.Reader),
            CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DownloadLatestDeviceTaggingTraceAsync_ReturnsStreamedFile()
    {
        var content = new MemoryStream(Encoding.UTF8.GetBytes("{\"sequence\":1}\n"));
        var function = CreateFunction(
            new FakeTaggingConfigurationStore(),
            new FakeStatusStore(),
            new FakeTraceReader(new RunTraceDownload(content, "application/x-ndjson", "trace.jsonl")));

        var result = await function.DownloadLatestDeviceTaggingTraceAsync(
            AuthenticatedRequest(ManagementApiRoles.TaggingOperator),
            CancellationToken.None);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Same(content, file.FileStream);
        Assert.Equal("trace.jsonl", file.FileDownloadName);
    }

    [Fact]
    public async Task RunDeviceTaggingAsync_RejectsMissingEasyAuthPrincipal()
    {
        var function = CreateFunction(new FakeTaggingConfigurationStore(), new FakeStatusStore(), new FakeTraceReader());

        var result = await function.RunDeviceTaggingAsync(
            new DefaultHttpContext().Request,
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task InitializeActiveDirectoryCredentialAsync_StoresPasswordWithoutReturningIt()
    {
        var initializer = new FakeCredentialInitializer();
        var function = CreateFunction(
            new FakeTaggingConfigurationStore(),
            new FakeStatusStore(),
            new FakeTraceReader(),
            credentialInitializer: initializer);
        var request = AuthenticatedRequest(ManagementApiRoles.PasswordRotationAdministrator);
        request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""{"password":"Initial!Password1"}"""));

        var result = await function.InitializeActiveDirectoryCredentialAsync(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Initial!Password1", initializer.Password);
        Assert.DoesNotContain("Initial!Password1", JsonSerializer.Serialize(ok.Value), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeActiveDirectoryCredentialAsync_RejectsReaderRole()
    {
        var initializer = new FakeCredentialInitializer();
        var function = CreateFunction(
            new FakeTaggingConfigurationStore(),
            new FakeStatusStore(),
            new FakeTraceReader(),
            credentialInitializer: initializer);
        var request = AuthenticatedRequest(ManagementApiRoles.Reader);
        request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""{"password":"Initial!Password1"}"""));

        var result = await function.InitializeActiveDirectoryCredentialAsync(request, CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Null(initializer.Password);
    }

    [Fact]
    public async Task InitializeActiveDirectoryCredentialAsync_ReturnsConflictWhenAlreadyInitialized()
    {
        var initializer = new FakeCredentialInitializer
        {
            Exception = new ActiveDirectoryCredentialAlreadyInitializedException()
        };
        var function = CreateFunction(
            new FakeTaggingConfigurationStore(),
            new FakeStatusStore(),
            new FakeTraceReader(),
            credentialInitializer: initializer);
        var request = AuthenticatedRequest(ManagementApiRoles.PasswordRotationAdministrator);
        request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""{"password":"Initial!Password1"}"""));

        var result = await function.InitializeActiveDirectoryCredentialAsync(request, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task RunDeviceTaggingAsync_QueuesRequestAndReturnsAccepted()
    {
        var statusStore = new FakeStatusStore();
        var queue = new FakeRequestQueue();
        var function = CreateFunction(
            new FakeTaggingConfigurationStore(),
            statusStore,
            new FakeTraceReader(),
            requestQueue: queue);

        var result = await function.RunDeviceTaggingAsync(
            AuthenticatedRequest(ManagementApiRoles.TaggingOperator),
            CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.Equal("/api/management/status/device-tagging", accepted.Location);
        var (operation, request) = Assert.Single(queue.Requests);
        Assert.Equal(OperationNames.DeviceTagging, operation);
        Assert.Equal(request.InvocationId, statusStore.QueuedInvocationId);
    }

    [Fact]
    public async Task RunPasswordRotationAsync_QueuesPasswordRotationRequest()
    {
        var queue = new FakeRequestQueue();
        var function = CreateFunction(
            new FakeTaggingConfigurationStore(),
            new FakeStatusStore(),
            new FakeTraceReader(),
            requestQueue: queue);

        var result = await function.RunPasswordRotationAsync(
            AuthenticatedRequest(ManagementApiRoles.PasswordRotationAdministrator),
            CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        Assert.Equal(OperationNames.PasswordRotation, Assert.Single(queue.Requests).Operation);
    }

    [Fact]
    public async Task RunDeviceTaggingAsync_ReturnsConflictWithoutQueueingWhenLockIsHeld()
    {
        var statusStore = new FakeStatusStore();
        var queue = new FakeRequestQueue();
        var function = CreateFunction(
            new FakeTaggingConfigurationStore(),
            statusStore,
            new FakeTraceReader(),
            new FakeOperationLock { Held = true },
            queue);

        var result = await function.RunDeviceTaggingAsync(
            AuthenticatedRequest(ManagementApiRoles.TaggingOperator),
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Empty(queue.Requests);
        Assert.Null(statusStore.QueuedInvocationId);
    }

    [Fact]
    public async Task RunPasswordRotationAsync_RejectsReaderRole()
    {
        var queue = new FakeRequestQueue();
        var function = CreateFunction(
            new FakeTaggingConfigurationStore(),
            new FakeStatusStore(),
            new FakeTraceReader(),
            requestQueue: queue);

        var result = await function.RunPasswordRotationAsync(
            AuthenticatedRequest(ManagementApiRoles.Reader),
            CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Empty(queue.Requests);
    }

    private static ManagementApiFunction CreateFunction(
        ITaggingConfigurationStore configurationStore,
        IOperationStatusStore statusStore,
        IRunTraceReader traceReader,
        IOperationLock? operationLock = null,
        IOperationRequestQueue? requestQueue = null,
        IActiveDirectoryCredentialInitializer? credentialInitializer = null,
        IManagementApiAuthorizer? authorizer = null,
        IActiveDirectoryRuleValidator? ruleValidator = null) =>
        new(
            new TaggingConfigurationService(
                configurationStore,
                ruleValidator ?? new AllowAllActiveDirectoryRuleValidator()),
            statusStore,
            traceReader,
            operationLock ?? new FakeOperationLock(),
            requestQueue ?? new FakeRequestQueue(),
            TimeProvider.System,
            credentialInitializer ?? new FakeCredentialInitializer(),
            authorizer ?? new ApplicationRoleAuthorizer());

    private sealed class AllowAllActiveDirectoryRuleValidator : IActiveDirectoryRuleValidator
    {
        public Task ValidateRulesAsync(
            IReadOnlyList<TaggingRuleOptions> rules,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class RejectingActiveDirectoryRuleValidator : IActiveDirectoryRuleValidator
    {
        public Task ValidateRulesAsync(
            IReadOnlyList<TaggingRuleOptions> rules,
            CancellationToken cancellationToken) =>
            throw new TaggingConfigurationValidationException(
                ["The rule targets a domain outside the configured forest."]);
    }

    private static HttpRequest AuthenticatedRequest(params string[] roles)
    {
        var request = new DefaultHttpContext().Request;
        var claims = new List<object> { new { typ = "oid", val = "test" } };
        claims.AddRange(roles.Select(role => (object)new { typ = "roles", val = role }));
        request.Headers["X-MS-CLIENT-PRINCIPAL"] = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { auth_typ = "aad", claims })));
        return request;
    }

    private sealed class ApplicationRoleAuthorizer : IManagementApiAuthorizer
    {
        public Task<EasyAuthAuthorizationResult> AuthorizeAsync(
            HttpRequest request,
            IReadOnlyCollection<string> requiredRoles,
            CancellationToken cancellationToken) =>
            Task.FromResult(EasyAuthPrincipalValidator.Authorize(
                request,
                [.. requiredRoles]));
    }

    private sealed class FakeTaggingConfigurationStore : ITaggingConfigurationStore
    {
        private TaggingConfigurationSnapshot _snapshot = new(
            [CreateRule()],
            "store-etag",
            DateTimeOffset.UtcNow,
            "test");

        public string? LastExpectedETag { get; private set; }

        public Task<IReadOnlyList<TaggingRuleOptions>> GetRulesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_snapshot.Rules);

        public Task<TaggingConfigurationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_snapshot);

        public Task<TaggingConfigurationSnapshot> ReplaceAsync(
            IReadOnlyList<TaggingRuleOptions> rules,
            string? expectedETag,
            CancellationToken cancellationToken)
        {
            LastExpectedETag = expectedETag;
            _snapshot = new TaggingConfigurationSnapshot(rules, "new-etag", DateTimeOffset.UtcNow, "test");
            return Task.FromResult(_snapshot);
        }
    }

    private sealed class FakeStatusStore : IOperationStatusStore
    {
        public string? QueuedInvocationId { get; private set; }

        public Task QueueAsync(string operation, string invocationId, CancellationToken cancellationToken)
        {
            QueuedInvocationId = invocationId;
            return Task.CompletedTask;
        }

        public Task RejectQueuedAsync(
            string operation,
            string invocationId,
            Exception reason,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartAsync(string operation, string invocationId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task CompleteAsync(
            string operation,
            string invocationId,
            IReadOnlyDictionary<string, object?> result,
            bool passwordChanged,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task FailAsync(
            string operation,
            string invocationId,
            Exception exception,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<OperationRunStatus?> GetAsync(string operation, CancellationToken cancellationToken) =>
            Task.FromResult<OperationRunStatus?>(null);
    }

    private sealed class FakeTraceReader(RunTraceDownload? trace = null) : IRunTraceReader
    {
        public Task<RunTraceDownload?> OpenLatestAsync(CancellationToken cancellationToken) =>
            Task.FromResult(trace);
    }

    private sealed class FakeOperationLock : IOperationLock
    {
        public bool Held { get; init; }

        public Task<IOperationLease?> TryAcquireAsync(string operation, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> IsHeldAsync(string operation, CancellationToken cancellationToken) =>
            Task.FromResult(Held);
    }

    private sealed class FakeRequestQueue : IOperationRequestQueue
    {
        public List<(string Operation, OperationRequest Request)> Requests { get; } = [];

        public Task EnqueueAsync(
            string operation,
            OperationRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add((operation, request));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCredentialInitializer : IActiveDirectoryCredentialInitializer
    {
        public string? Password { get; private set; }

        public Exception? Exception { get; init; }

        public Task InitializeAsync(string password, CancellationToken cancellationToken)
        {
            Password = password;
            return Exception is null ? Task.CompletedTask : Task.FromException(Exception);
        }
    }

    private static TaggingRuleOptions CreateRule() => new()
    {
        Name = "Servers",
        Description = "Test",
        BaseDn = "OU=Servers,DC=example,DC=com",
        ExtensionAttributes = new Dictionary<string, string>
        {
            ["extensionAttribute1"] = "Managed"
        }
    };
}