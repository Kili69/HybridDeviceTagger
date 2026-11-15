using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using HybridDeviceTagger.Services;
using Moq;

namespace HybridDeviceTagger.Tests;

public sealed class ActiveDirectoryCredentialInitializerTests
{
    [Fact]
    public async Task InitializeAsync_ValidatesBindBeforeStoringPassword()
    {
        var store = new FakeCredentialStore();
        var passwordClient = new FakePasswordClient(Success());
        var initializer = CreateInitializer(store, passwordClient);

        await initializer.InitializeAsync("Initial!Password1", CancellationToken.None);

        Assert.Equal("svc-tagger@example.com", passwordClient.Credential?.Username);
        Assert.Equal("Initial!Password1", passwordClient.Credential?.Password);
        Assert.Equal("Initial!Password1", store.InitialPassword);
    }

    [Fact]
    public async Task InitializeAsync_UsesManagedServiceAccountOverride()
    {
        var passwordClient = new FakePasswordClient(Success());
        var initializer = CreateInitializer(
            new FakeCredentialStore(),
            passwordClient,
            new ActiveDirectoryConfigurationSnapshot("example.com", "\"v\"", null, "blob", "renamed@example.com"));

        await initializer.InitializeAsync("Initial!Password1", CancellationToken.None);

        Assert.Equal("renamed@example.com", passwordClient.Credential?.Username);
    }

    [Theory]
    [InlineData(true, "cannot authenticate")]
    [InlineData(false, "certificate presented by 'dc01.example.com' failed")]
    public async Task InitializeAsync_DoesNotStorePasswordAndReportsActualCause(bool invalidCredentials, string expectedMessage)
    {
        var store = new FakeCredentialStore();
        var failure = new ActiveDirectoryAccessValidationResult(
            false,
            invalidCredentials,
            invalidCredentials
                ? "Invalid Credentials"
                : "TLS validation of the certificate presented by 'dc01.example.com' failed: UntrustedRoot",
            [new ActiveDirectoryLdapConnectionAttempt("dc01.example.com", 636, "Ldaps", false, invalidCredentials ? 49 : -2, "failure")]);
        var initializer = CreateInitializer(store, new FakePasswordClient(failure));

        var exception = await Assert.ThrowsAsync<ActiveDirectoryCredentialValidationException>(
            () => initializer.InitializeAsync("WrongPassword", CancellationToken.None));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("WrongPassword", exception.Message, StringComparison.Ordinal);
        Assert.Null(store.InitialPassword);
    }

    private static ActiveDirectoryAccessValidationResult Success() =>
        new(true, false, null, [new ActiveDirectoryLdapConnectionAttempt("dc01.example.com", 636, "Ldaps", true, null, null)]);

    private static ActiveDirectoryCredentialInitializer CreateInitializer(
        IActiveDirectoryCredentialStore store,
        IActiveDirectoryPasswordClient passwordClient,
        ActiveDirectoryConfigurationSnapshot? snapshot = null)
    {
        var configurationStore = new Mock<IActiveDirectoryConfigurationStore>();
        configurationStore.Setup(value => value.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot ?? new ActiveDirectoryConfigurationSnapshot("example.com", null, null, "appSetting"));
        return new(
            store,
            passwordClient,
            Microsoft.Extensions.Options.Options.Create(new ActiveDirectoryOptions
            {
                BindUsername = "svc-tagger@example.com"
            }),
            configurationStore.Object);
    }

    private sealed class FakeCredentialStore : IActiveDirectoryCredentialStore
    {
        public string? InitialPassword { get; private set; }

        public Task<StoredDirectoryCredential> InitializeCurrentAsync(
            string password,
            CancellationToken cancellationToken)
        {
            InitialPassword = password;
            return Task.FromResult(new StoredDirectoryCredential("svc-tagger@example.com", password, null));
        }

        public Task<StoredDirectoryCredential> GetCurrentAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StoredDirectoryCredential?> GetNextAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StoredDirectoryCredential> StageNextAsync(
            string password,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task PromoteAsync(string password, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StoredDirectoryCredential> ReplaceCurrentAsync(string password, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ClearNextAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakePasswordClient(ActiveDirectoryAccessValidationResult result) : IActiveDirectoryPasswordClient
    {
        public StoredDirectoryCredential? Credential { get; private set; }

        public Task<bool> CanBindAsync(
            StoredDirectoryCredential credential,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> CanBindSecurelyAsync(
            StoredDirectoryCredential credential,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ChangePasswordAsync(
            StoredDirectoryCredential currentCredential,
            string newPassword,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ActiveDirectoryAccessValidationResult> ValidateAccessAsync(
            StoredDirectoryCredential credential,
            CancellationToken cancellationToken)
        {
            Credential = credential;
            return Task.FromResult(result);
        }
    }
}
