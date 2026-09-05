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
        var request = AuthenticatedRequest();
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
    public async Task GetDeviceTaggingStatusAsync_ReturnsNotFoundBeforeFirstRun()
    {
        var function = CreateFunction(new FakeTaggingConfigurationStore(), new FakeStatusStore(), new FakeTraceReader());

        var result = await function.GetDeviceTaggingStatusAsync(
            AuthenticatedRequest(),
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
            AuthenticatedRequest(),
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
    public async Task RunDeviceTaggingAsync_InvokesRunner()
    {
        var runner = new FakeDeviceTaggingRunner();
        var function = CreateFunction(
            new FakeTaggingConfigurationStore(),
            new FakeStatusStore(),
            new FakeTraceReader(),
            runner);

        var result = await function.RunDeviceTaggingAsync(
            AuthenticatedRequest(),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(runner.InvocationId);
        Assert.False(runner.IsPastDue);
    }

    [Fact]
    public async Task RunPasswordRotationAsync_ForcesRotation()
    {
        var runner = new FakePasswordRotationRunner();
        var function = CreateFunction(
            new FakeTaggingConfigurationStore(),
            new FakeStatusStore(),
            new FakeTraceReader(),
            passwordRotationRunner: runner);

        var result = await function.RunPasswordRotationAsync(
            AuthenticatedRequest(),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(runner.InvocationId);
        Assert.False(runner.IsPastDue);
        Assert.True(runner.Force);
    }

    [Fact]
    public async Task RunDeviceTaggingAsync_ReturnsConflictWhenAlreadyRunning()
    {
        var runner = new FakeDeviceTaggingRunner { AlreadyRunning = true };
        var function = CreateFunction(
            new FakeTaggingConfigurationStore(),
            new FakeStatusStore(),
            new FakeTraceReader(),
            runner);

        var result = await function.RunDeviceTaggingAsync(
            AuthenticatedRequest(),
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    private static ManagementApiFunction CreateFunction(
        ITaggingConfigurationStore configurationStore,
        IOperationStatusStore statusStore,
        IRunTraceReader traceReader,
        IDeviceTaggingRunner? deviceTaggingRunner = null,
        IPasswordRotationRunner? passwordRotationRunner = null) =>
        new(
            new TaggingConfigurationService(configurationStore),
            statusStore,
            traceReader,
            deviceTaggingRunner ?? new FakeDeviceTaggingRunner(),
            passwordRotationRunner ?? new FakePasswordRotationRunner());

    private static HttpRequest AuthenticatedRequest()
    {
        var request = new DefaultHttpContext().Request;
        request.Headers["X-MS-CLIENT-PRINCIPAL"] = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("""{"auth_typ":"aad","claims":[{"typ":"oid","val":"test"}]}"""));
        return request;
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

    private sealed class FakeDeviceTaggingRunner : IDeviceTaggingRunner
    {
        public string? InvocationId { get; private set; }

        public bool IsPastDue { get; private set; }

        public bool AlreadyRunning { get; init; }

        public Task<TaggingResult> RunAsync(
            string invocationId,
            bool isPastDue,
            CancellationToken cancellationToken)
        {
            if (AlreadyRunning)
            {
                throw new OperationAlreadyRunningException(OperationNames.DeviceTagging);
            }

            InvocationId = invocationId;
            IsPastDue = isPastDue;
            return Task.FromResult(new TaggingResult(1, 1, 0, 0, 0));
        }
    }

    private sealed class FakePasswordRotationRunner : IPasswordRotationRunner
    {
        public string? InvocationId { get; private set; }

        public bool IsPastDue { get; private set; }

        public bool Force { get; private set; }

        public Task<PasswordRotationResult> RunAsync(
            string invocationId,
            bool isPastDue,
            bool force,
            CancellationToken cancellationToken)
        {
            InvocationId = invocationId;
            IsPastDue = isPastDue;
            Force = force;
            return Task.FromResult(new PasswordRotationResult(PasswordRotationOutcome.Rotated, 10));
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