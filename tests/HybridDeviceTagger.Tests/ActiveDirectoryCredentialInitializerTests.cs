using HybridDeviceTagger.Models;
using HybridDeviceTagger.Options;
using HybridDeviceTagger.Services;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Tests;

public sealed class ActiveDirectoryCredentialInitializerTests
{
    [Fact]
    public async Task InitializeAsync_ValidatesBindBeforeStoringPassword()
    {
        var store = new FakeCredentialStore();
        var passwordClient = new FakePasswordClient { CanBind = true };
        var initializer = CreateInitializer(store, passwordClient);

        await initializer.InitializeAsync("Initial!Password1", CancellationToken.None);

        Assert.Equal("svc-tagger@example.com", passwordClient.Credential?.Username);
        Assert.Equal("Initial!Password1", passwordClient.Credential?.Password);
        Assert.Equal("Initial!Password1", store.InitialPassword);
    }

    [Fact]
    public async Task InitializeAsync_DoesNotStorePasswordWhenBindFails()
    {
        var store = new FakeCredentialStore();
        var initializer = CreateInitializer(store, new FakePasswordClient { CanBind = false });

        await Assert.ThrowsAsync<ActiveDirectoryCredentialValidationException>(
            () => initializer.InitializeAsync("WrongPassword", CancellationToken.None));

        Assert.Null(store.InitialPassword);
    }

    private static ActiveDirectoryCredentialInitializer CreateInitializer(
        IActiveDirectoryCredentialStore store,
        IActiveDirectoryPasswordClient passwordClient) =>
        new(
            store,
            passwordClient,
            Microsoft.Extensions.Options.Options.Create(new ActiveDirectoryOptions
            {
                BindUsername = "svc-tagger@example.com"
            }));

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
    }

    private sealed class FakePasswordClient : IActiveDirectoryPasswordClient
    {
        public bool CanBind { get; init; }

        public StoredDirectoryCredential? Credential { get; private set; }

        public Task<bool> CanBindAsync(
            StoredDirectoryCredential credential,
            CancellationToken cancellationToken)
        {
            Credential = credential;
            return Task.FromResult(CanBind);
        }

        public Task<bool> CanBindSecurelyAsync(
            StoredDirectoryCredential credential,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ChangePasswordAsync(
            StoredDirectoryCredential currentCredential,
            string newPassword,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}