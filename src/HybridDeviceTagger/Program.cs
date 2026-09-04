using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using HybridDeviceTagger.Options;
using HybridDeviceTagger.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Export worker and application telemetry through Application Insights when Azure supplies a
// connection string. Local development can run with the worker defaults only.
var telemetry = builder.Services.AddOpenTelemetry()
    .UseFunctionsWorkerDefaults();
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    telemetry.UseAzureMonitorExporter();
}

// Validate all external configuration before either timer function can run. This turns missing
// LDAP, Key Vault, tagging, or rotation settings into startup failures instead of partial runs.
builder.Services
    .AddOptions<ActiveDirectoryOptions>()
    .BindConfiguration(ActiveDirectoryOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<TaggingOptions>()
    .BindConfiguration(TaggingOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<KeyVaultOptions>()
    .BindConfiguration(KeyVaultOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<PasswordRotationOptions>()
    .BindConfiguration(PasswordRotationOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// DefaultAzureCredential uses managed identity in Azure and the developer credential chain
// locally; no Azure credential is stored in application configuration.
builder.Services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<
        Microsoft.Extensions.Options.IOptions<KeyVaultOptions>>().Value;
    var credential = serviceProvider.GetRequiredService<TokenCredential>();
    return new SecretClient(new Uri(options.VaultUri), credential);
});
builder.Services.AddSingleton<IActiveDirectoryCredentialStore, KeyVaultActiveDirectoryCredentialStore>();
builder.Services.AddSingleton<ActiveDirectoryLdapConnectionFactory>();
builder.Services.AddSingleton<IActiveDirectoryDeviceSource, LdapActiveDirectoryDeviceSource>();
builder.Services.AddSingleton<IActiveDirectoryPasswordClient, LdapActiveDirectoryPasswordClient>();
builder.Services.AddSingleton<IPasswordGenerator, SecurePasswordGenerator>();
builder.Services.AddTransient<PasswordRotationService>();
builder.Services.AddHttpClient("MicrosoftGraph", client =>
{
    client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
});
builder.Services.AddScoped(serviceProvider =>
{
    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    var credential = serviceProvider.GetRequiredService<TokenCredential>();
    return new MicrosoftGraphDeviceClient(
        httpClientFactory.CreateClient("MicrosoftGraph"),
        credential);
});
builder.Services.AddScoped<IEntraDeviceClient>(serviceProvider =>
    serviceProvider.GetRequiredService<MicrosoftGraphDeviceClient>());
builder.Services.AddScoped<IEntraMembershipClient>(serviceProvider =>
    serviceProvider.GetRequiredService<MicrosoftGraphDeviceClient>());
builder.Services.AddSingleton(serviceProvider =>
{
    const string containerName = "membership-ledger";
    var connectionString = builder.Configuration["AzureWebJobsStorage"];
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        return new BlobContainerClient(connectionString, containerName);
    }

    var accountName = builder.Configuration["AzureWebJobsStorage__accountName"];
    if (string.IsNullOrWhiteSpace(accountName))
    {
        throw new InvalidOperationException(
            "AzureWebJobsStorage or AzureWebJobsStorage__accountName must be configured.");
    }

    var credential = serviceProvider.GetRequiredService<TokenCredential>();
    return new BlobContainerClient(
        new Uri($"https://{accountName}.blob.core.windows.net/{containerName}"),
        credential);
});
builder.Services.AddSingleton<IDeviceMembershipLedger, BlobDeviceMembershipLedger>();
builder.Services.AddScoped<DeviceMembershipReconciliationService>();
builder.Services.AddTransient<DeviceTaggingService>();

builder.Build().Run();
