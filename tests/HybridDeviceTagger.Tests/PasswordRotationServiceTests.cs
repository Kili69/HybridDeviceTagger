using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using HybridDeviceTagger.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HybridDeviceTagger.Tests;

public sealed class PasswordRotationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RotateIfDueAsync_DoesNotRotateRecentPassword()
    {
        var store = new StubCredentialStore(CreateCredential("current", Now.AddDays(-10)), Now);
        var passwordClient = new StubPasswordClient("current");
        var service = CreateService(store, passwordClient);

        var result = await service.RotateIfDueAsync(CancellationToken.None);

        Assert.Equal(PasswordRotationOutcome.NotDue, result.Outcome);
        Assert.Equal(10, result.CurrentPasswordAgeDays);
        Assert.Equal(0, passwordClient.ChangeCount);
        Assert.Equal(0, store.StageCount);
    }

    [Fact]
    public async Task RotateIfDueAsync_UsesRuntimeRotationInterval()
    {
        var store = new StubCredentialStore(CreateCredential("current", Now.AddDays(-10)), Now);
        var passwordClient = new StubPasswordClient("current");
        var schedule = new Mock<IOperationScheduleStore>();
        schedule.Setup(value => value.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationScheduleSnapshot(15, 7, 29, "\"v\"", null, "blob"));
        var service = CreateService(store, passwordClient, schedule.Object);

        var result = await service.RotateIfDueAsync(CancellationToken.None);

        Assert.Equal(PasswordRotationOutcome.Rotated, result.Outcome);
        Assert.Equal(1, passwordClient.ChangeCount);
    }

    [Fact]
    public async Task RotateNowAsync_DefersUntilMinimumPasswordAgeElapsed()
    {
        var store = new StubCredentialStore(CreateCredential("current", Now.AddDays(-10)), Now);
        var passwordClient = new StubPasswordClient("current")
        {
            Window = new PasswordChangeWindow(Now.AddMinutes(-17), TimeSpan.FromDays(1), "domain")
        };
        var service = CreateService(store, passwordClient);

        var result = await service.RotateNowAsync(CancellationToken.None);

        Assert.Equal(PasswordRotationOutcome.DeferredMinimumPasswordAge, result.Outcome);
        Assert.Equal(Now.AddMinutes(-17).AddDays(1), result.EarliestChangeUtc);
        Assert.Equal(0, store.StageCount);
        Assert.Equal(0, passwordClient.ChangeCount);
    }

    [Fact]
    public async Task RotateNowAsync_ExplainsPolicyRejectionAndKeepsCurrentPassword()
    {
        var store = new StubCredentialStore(CreateCredential("current", Now.AddDays(-10)), Now);
        var passwordClient = new StubPasswordClient("current") { RejectChangeByPolicy = true };
        var service = CreateService(store, passwordClient);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RotateNowAsync(CancellationToken.None));

        Assert.Contains("password policy", exception.Message, StringComparison.Ordinal);
        Assert.Equal("current", store.Current.Password);
    }

    [Theory]
    [InlineData("-864000000000", 1.0)]
    [InlineData("0", 0.0)]
    [InlineData("bad", null)]
    public void ParseInterval_ConvertsActiveDirectoryAges(string value, double? days) =>
        Assert.Equal(days is null ? null : TimeSpan.FromDays(days.Value), LdapActiveDirectoryPasswordClient.ParseInterval(value));

    [Fact]
    public async Task RotateNowAsync_RotatesRecentPassword()
    {
        var store = new StubCredentialStore(CreateCredential("current", Now.AddDays(-10)), Now);
        var passwordClient = new StubPasswordClient("current");
        var service = CreateService(store, passwordClient);

        var result = await service.RotateNowAsync(CancellationToken.None);

        Assert.Equal(PasswordRotationOutcome.Rotated, result.Outcome);
        Assert.Equal(10, result.CurrentPasswordAgeDays);
        Assert.Equal(1, store.StageCount);
        Assert.Equal(1, passwordClient.ChangeCount);
        Assert.Equal("generated-password", store.Current.Password);
    }

    [Fact]
    public async Task RotateIfDueAsync_StagesChangesValidatesAndPromotesPassword()
    {
        var store = new StubCredentialStore(CreateCredential("current", Now.AddDays(-25)), Now);
        var passwordClient = new StubPasswordClient("current");
        var service = CreateService(store, passwordClient);

        var result = await service.RotateIfDueAsync(CancellationToken.None);

        Assert.Equal(PasswordRotationOutcome.Rotated, result.Outcome);
        Assert.Equal(1, store.StageCount);
        Assert.Equal(1, passwordClient.ChangeCount);
        Assert.Equal("generated-password", passwordClient.ActivePassword);
        Assert.Equal("generated-password", store.Current.Password);
    }

    [Fact]
    public async Task RotateIfDueAsync_PromotesWorkingNextWhenCurrentCannotBind()
    {
        var store = new StubCredentialStore(CreateCredential("stale", Now.AddDays(-26)), Now)
        {
            Next = CreateCredential("working-next", Now.AddDays(-1))
        };
        var passwordClient = new StubPasswordClient("working-next");
        var service = CreateService(store, passwordClient);

        var result = await service.RotateIfDueAsync(CancellationToken.None);

        Assert.Equal(PasswordRotationOutcome.RecoveredFromNext, result.Outcome);
        Assert.Equal("working-next", store.Current.Password);
        Assert.Equal(0, store.StageCount);
        Assert.Equal(0, passwordClient.ChangeCount);
    }

    [Fact]
    public async Task RotateIfDueAsync_DefersWithoutStagingWhenLdapsIsUnavailable()
    {
        var store = new StubCredentialStore(CreateCredential("current", Now.AddDays(-25)), Now);
        var passwordClient = new StubPasswordClient("current")
        {
            SecureConnectionAvailable = false
        };
        var service = CreateService(store, passwordClient);

        var result = await service.RotateIfDueAsync(CancellationToken.None);

        Assert.Equal(PasswordRotationOutcome.DeferredNoSecureConnection, result.Outcome);
        Assert.Equal(25, result.CurrentPasswordAgeDays);
        Assert.Equal(0, store.StageCount);
        Assert.Equal(0, passwordClient.ChangeCount);
        Assert.Equal("current", store.Current.Password);
    }

    [Fact]
    public void Generate_ContainsRequiredCharacterClasses()
    {
        var password = new SecurePasswordGenerator().Generate(40);

        Assert.Equal(40, password.Length);
        Assert.Contains(password, char.IsUpper);
        Assert.Contains(password, char.IsLower);
        Assert.Contains(password, char.IsDigit);
        Assert.Contains(password, character => "!#$%&*+-=?@_".Contains(character));
    }

    [Fact]
    public async Task PasswordRotationFunction_DoesNotRotateWhileAnotherInstanceHoldsLock()
    {
        var store = new StubCredentialStore(CreateCredential("current", Now.AddDays(-30)), Now);
        var passwordClient = new StubPasswordClient("current");
        var statusStore = new Mock<IOperationStatusStore>();
        var operationLock = new Mock<IOperationLock>();
        operationLock
            .Setup(value => value.TryAcquireAsync(OperationNames.PasswordRotation, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IOperationLease?)null);
        var function = new PasswordRotationFunction(
            CreateService(store, passwordClient),
            statusStore.Object,
            operationLock.Object,
            NullLogger<PasswordRotationFunction>.Instance);

        var result = await function.RunOperationAsync("request", false, true, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, passwordClient.ChangeCount);
        Assert.Equal(0, store.StageCount);
        statusStore.Verify(
            value => value.RejectQueuedAsync(
                OperationNames.PasswordRotation,
                "request",
                It.IsAny<OperationAlreadyRunningException>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PasswordRotationFunction_RotatesAndReleasesAcquiredLock()
    {
        var store = new StubCredentialStore(CreateCredential("current", Now.AddDays(-30)), Now);
        var passwordClient = new StubPasswordClient("current");
        var lease = new Mock<IOperationLease>();
        lease.SetupGet(value => value.LeaseLost).Returns(CancellationToken.None);
        var operationLock = new Mock<IOperationLock>();
        operationLock
            .Setup(value => value.TryAcquireAsync(OperationNames.PasswordRotation, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lease.Object);
        var function = new PasswordRotationFunction(
            CreateService(store, passwordClient),
            new Mock<IOperationStatusStore>().Object,
            operationLock.Object,
            NullLogger<PasswordRotationFunction>.Instance);

        var result = await function.RunOperationAsync("timer", false, false, CancellationToken.None);

        Assert.Equal(PasswordRotationOutcome.Rotated, result?.Outcome);
        lease.Verify(value => value.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task PasswordRotationFunction_MarksQueuedRequestFailedWhenLockCannotBeRequested()
    {
        var failure = new InvalidOperationException("storage unavailable");
        var statusStore = new Mock<IOperationStatusStore>();
        var operationLock = new Mock<IOperationLock>();
        operationLock
            .Setup(value => value.TryAcquireAsync(OperationNames.PasswordRotation, It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);
        var function = new PasswordRotationFunction(
            CreateService(
                new StubCredentialStore(CreateCredential("current", Now.AddDays(-30)), Now),
                new StubPasswordClient("current")),
            statusStore.Object,
            operationLock.Object,
            NullLogger<PasswordRotationFunction>.Instance);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            function.RunOperationAsync("request", false, true, CancellationToken.None));

        Assert.Same(failure, thrown);
        statusStore.Verify(
            value => value.RejectQueuedAsync(
                OperationNames.PasswordRotation,
                "request",
                failure,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static PasswordRotationService CreateService(
        StubCredentialStore store,
        StubPasswordClient passwordClient,
        IOperationScheduleStore? scheduleStore = null) =>
        new(
            store,
            passwordClient,
            new StubPasswordGenerator(),
            Microsoft.Extensions.Options.Options.Create(new PasswordRotationOptions
            {
                RotateAfterDays = 25,
                PasswordLength = 40,
                SecretExpiryDays = 35
            }),
            new FixedTimeProvider(Now),
            NullLogger<PasswordRotationService>.Instance,
            scheduleStore);

    private static StoredDirectoryCredential CreateCredential(
        string password,
        DateTimeOffset createdOn) =>
        new("svc-HybridDeviceTagger@example.com", password, createdOn);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class StubPasswordGenerator : IPasswordGenerator
    {
        public string Generate(int length) => "generated-password";
    }

    private sealed class StubPasswordClient : IActiveDirectoryPasswordClient
    {
        public StubPasswordClient(string activePassword)
        {
            ActivePassword = activePassword;
        }

        public string ActivePassword { get; private set; }

        public bool SecureConnectionAvailable { get; init; } = true;

        public PasswordChangeWindow? Window { get; init; }

        public bool RejectChangeByPolicy { get; init; }

        public Task<PasswordChangeWindow?> GetPasswordChangeWindowAsync(
            StoredDirectoryCredential credential,
            CancellationToken cancellationToken) =>
            Task.FromResult(Window);

        public int ChangeCount { get; private set; }

        public Task<bool> CanBindAsync(
            StoredDirectoryCredential credential,
            CancellationToken cancellationToken) =>
            Task.FromResult(credential.Password == ActivePassword);

        public Task<bool> CanBindSecurelyAsync(
            StoredDirectoryCredential credential,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                SecureConnectionAvailable && credential.Password == ActivePassword);

        public Task ChangePasswordAsync(
            StoredDirectoryCredential currentCredential,
            string newPassword,
            CancellationToken cancellationToken)
        {
            Assert.Equal(ActivePassword, currentCredential.Password);
            if (RejectChangeByPolicy)
            {
                throw new ActiveDirectoryLdapException("Constraint Violation", 19, new InvalidOperationException());
            }
            ActivePassword = newPassword;
            ChangeCount++;
            return Task.CompletedTask;
        }

        public Task<ActiveDirectoryAccessValidationResult> ValidateAccessAsync(
            StoredDirectoryCredential credential,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubCredentialStore : IActiveDirectoryCredentialStore
    {
        private readonly DateTimeOffset _now;

        public StubCredentialStore(StoredDirectoryCredential current, DateTimeOffset now)
        {
            Current = current;
            _now = now;
        }

        public StoredDirectoryCredential Current { get; private set; }

        public StoredDirectoryCredential? Next { get; set; }

        public int StageCount { get; private set; }

        public Task<StoredDirectoryCredential> InitializeCurrentAsync(
            string password,
            CancellationToken cancellationToken) =>
            throw new ActiveDirectoryCredentialAlreadyInitializedException();

        public Task<StoredDirectoryCredential> GetCurrentAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(Current);

        public Task<StoredDirectoryCredential?> GetNextAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(Next);

        public Task<StoredDirectoryCredential> StageNextAsync(
            string password,
            CancellationToken cancellationToken)
        {
            Next = new StoredDirectoryCredential(Current.Username, password, _now);
            StageCount++;
            return Task.FromResult(Next);
        }

        public Task PromoteAsync(string password, CancellationToken cancellationToken)
        {
            Current = new StoredDirectoryCredential(Current.Username, password, _now);
            return Task.CompletedTask;
        }

        public Task<StoredDirectoryCredential> ReplaceCurrentAsync(string password, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ClearNextAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}