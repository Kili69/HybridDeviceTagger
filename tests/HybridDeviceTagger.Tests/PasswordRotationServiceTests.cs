using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using HybridDeviceTagger.Services;
using Microsoft.Extensions.Logging.Abstractions;

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
    public void Generate_ContainsRequiredCharacterClasses()
    {
        var password = new SecurePasswordGenerator().Generate(40);

        Assert.Equal(40, password.Length);
        Assert.Contains(password, char.IsUpper);
        Assert.Contains(password, char.IsLower);
        Assert.Contains(password, char.IsDigit);
        Assert.Contains(password, character => "!#$%&*+-=?@_".Contains(character));
    }

    private static PasswordRotationService CreateService(
        StubCredentialStore store,
        StubPasswordClient passwordClient) =>
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
            NullLogger<PasswordRotationService>.Instance);

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

        public int ChangeCount { get; private set; }

        public Task<bool> CanBindAsync(
            StoredDirectoryCredential credential,
            CancellationToken cancellationToken) =>
            Task.FromResult(credential.Password == ActivePassword);

        public Task ChangePasswordAsync(
            StoredDirectoryCredential currentCredential,
            string newPassword,
            CancellationToken cancellationToken)
        {
            Assert.Equal(ActivePassword, currentCredential.Password);
            ActivePassword = newPassword;
            ChangeCount++;
            return Task.CompletedTask;
        }
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
    }
}