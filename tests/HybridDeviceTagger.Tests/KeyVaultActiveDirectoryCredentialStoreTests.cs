using Azure;
using Azure.Security.KeyVault.Secrets;
using HybridDeviceTagger.Options;
using HybridDeviceTagger.Services;
using Moq;

namespace HybridDeviceTagger.Tests;

public sealed class KeyVaultActiveDirectoryCredentialStoreTests
{
    private const string CurrentName = "current";
    private const string NextName = "next";

    [Fact]
    public async Task InitializeCurrentAsync_ReplacesDeploymentPlaceholder()
    {
        var client = CreateClient(CurrentName, Placeholder(CurrentName));
        client
            .Setup(value => value.SetSecretAsync(It.IsAny<KeyVaultSecret>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((KeyVaultSecret secret, CancellationToken _) =>
                Response.FromValue(secret, Mock.Of<Response>()));

        var credential = await CreateStore(client).InitializeCurrentAsync("password", CancellationToken.None);

        Assert.Equal("password", credential.Password);
        client.Verify(
            value => value.SetSecretAsync(
                It.Is<KeyVaultSecret>(secret => secret.Name == CurrentName && secret.Properties.ContentType == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task InitializeCurrentAsync_NeverOverwritesRealPassword()
    {
        var client = CreateClient(CurrentName, new KeyVaultSecret(CurrentName, "existing"));

        await Assert.ThrowsAsync<ActiveDirectoryCredentialAlreadyInitializedException>(() =>
            CreateStore(client).InitializeCurrentAsync("password", CancellationToken.None));

        client.Verify(
            value => value.SetSecretAsync(It.IsAny<KeyVaultSecret>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PlaceholderSecrets_AreNotUsedAsCredentials()
    {
        var client = CreateClient(CurrentName, Placeholder(CurrentName));
        client
            .Setup(value => value.GetSecretAsync(NextName, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(Placeholder(NextName), Mock.Of<Response>()));
        var store = CreateStore(client);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetCurrentAsync(CancellationToken.None));
        Assert.Null(await store.GetNextAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ClearNextAsync_WritesPlaceholderAndCurrentUsesRenamedAccount()
    {
        var client = CreateClient(CurrentName, new KeyVaultSecret(CurrentName, "existing"));
        client
            .Setup(value => value.SetSecretAsync(It.IsAny<KeyVaultSecret>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((KeyVaultSecret secret, CancellationToken _) =>
                Response.FromValue(secret, Mock.Of<Response>()));
        var store = CreateStore(client, "renamed@example.com");

        await store.ClearNextAsync(CancellationToken.None);
        var current = await store.GetCurrentAsync(CancellationToken.None);

        client.Verify(
            value => value.SetSecretAsync(
                It.Is<KeyVaultSecret>(secret =>
                    secret.Name == NextName &&
                    secret.Properties.ContentType == KeyVaultActiveDirectoryCredentialStore.PlaceholderContentType),
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal("renamed@example.com", current.Username);
    }

    [Fact]
    public async Task ReplaceCurrentAsync_StoresPasswordBeforeFirstServiceAccountIsConfigured()
    {
        var client = CreateClient(CurrentName, new KeyVaultSecret(CurrentName, "stored"));
        client
            .Setup(value => value.SetSecretAsync(It.IsAny<KeyVaultSecret>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((KeyVaultSecret secret, CancellationToken _) =>
                Response.FromValue(secret, Mock.Of<Response>()));
        var store = CreateStore(client, bindUsername: string.Empty);

        var written = await store.ReplaceCurrentAsync("password", CancellationToken.None);

        Assert.Equal("password", written.Password);
        await Assert.ThrowsAsync<ActiveDirectoryNotConfiguredException>(() => store.GetCurrentAsync(CancellationToken.None));
    }

    private static KeyVaultSecret Placeholder(string name)
    {
        var secret = new KeyVaultSecret(name, Guid.NewGuid().ToString());
        secret.Properties.ContentType = KeyVaultActiveDirectoryCredentialStore.PlaceholderContentType;
        return secret;
    }

    private static Mock<SecretClient> CreateClient(string name, KeyVaultSecret secret)
    {
        var client = new Mock<SecretClient>();
        client
            .Setup(value => value.GetSecretAsync(name, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(secret, Mock.Of<Response>()));
        return client;
    }

    private static KeyVaultActiveDirectoryCredentialStore CreateStore(
        Mock<SecretClient> client,
        string? renamedAccount = null,
        string bindUsername = "svc@example.com")
    {
        var configurationStore = new Mock<IActiveDirectoryConfigurationStore>();
        configurationStore.Setup(value => value.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveDirectoryConfigurationSnapshot(
                "example.com", null, null, renamedAccount is null ? "appSetting" : "blob", renamedAccount));
        return new(
            client.Object,
            Microsoft.Extensions.Options.Options.Create(new ActiveDirectoryOptions
            {
                BindUsername = bindUsername,
                BindUserDistinguishedName = "CN=svc,DC=example,DC=com"
            }),
            Microsoft.Extensions.Options.Options.Create(new KeyVaultOptions
            {
                VaultUri = "https://example.vault.azure.net/",
                CurrentPasswordSecretName = CurrentName,
                NextPasswordSecretName = NextName
            }),
            Microsoft.Extensions.Options.Options.Create(new PasswordRotationOptions()),
            TimeProvider.System,
            configurationStore.Object);
    }
}
