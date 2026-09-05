/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using HybridDeviceTagger;
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
    var connectionString = builder.Configuration["AzureWebJobsStorage"];
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        return new BlobServiceClient(connectionString);
    }

    var accountName = builder.Configuration["AzureWebJobsStorage:accountName"];
    if (string.IsNullOrWhiteSpace(accountName))
    {
        throw new InvalidOperationException(
            "AzureWebJobsStorage or AzureWebJobsStorage__accountName must be configured.");
    }

    var credential = serviceProvider.GetRequiredService<TokenCredential>();
    return new BlobServiceClient(
        new Uri($"https://{accountName}.blob.core.windows.net"),
        credential);
});
builder.Services.AddSingleton<IDeviceMembershipLedger>(serviceProvider =>
    new BlobDeviceMembershipLedger(
        serviceProvider.GetRequiredService<BlobServiceClient>()
            .GetBlobContainerClient("membership-ledger")));
builder.Services.AddSingleton<BlobRunTraceStore>(serviceProvider =>
    new BlobRunTraceStore(
        serviceProvider.GetRequiredService<BlobServiceClient>()
            .GetBlobContainerClient("run-traces")
            .GetAppendBlobClient(BlobRunTraceStore.BlobName),
        serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BlobRunTraceStore>>()));
builder.Services.AddSingleton<IRunTraceStore>(serviceProvider =>
    serviceProvider.GetRequiredService<BlobRunTraceStore>());
builder.Services.AddSingleton<IRunTraceReader>(serviceProvider =>
    serviceProvider.GetRequiredService<BlobRunTraceStore>());
builder.Services.AddSingleton<IOperationStatusStore>(serviceProvider =>
    new BlobOperationStatusStore(
        serviceProvider.GetRequiredService<BlobServiceClient>()
            .GetBlobContainerClient("run-traces"),
        serviceProvider.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<OptionsTaggingRuleProvider>();
builder.Services.AddSingleton<BlobTaggingConfigurationStore>(serviceProvider =>
    new BlobTaggingConfigurationStore(
        serviceProvider.GetRequiredService<BlobServiceClient>()
            .GetBlobContainerClient("run-traces")
            .GetBlobClient(BlobTaggingConfigurationStore.BlobName),
        serviceProvider.GetRequiredService<OptionsTaggingRuleProvider>()));
builder.Services.AddSingleton<ITaggingConfigurationStore>(serviceProvider =>
    serviceProvider.GetRequiredService<BlobTaggingConfigurationStore>());
builder.Services.AddSingleton<ITaggingRuleProvider>(serviceProvider =>
    serviceProvider.GetRequiredService<BlobTaggingConfigurationStore>());
builder.Services.AddSingleton<TaggingConfigurationService>();
builder.Services.AddScoped<DeviceMembershipReconciliationService>();
builder.Services.AddTransient<DeviceTaggingService>();
builder.Services.AddScoped<DeviceTaggerFunction>();
builder.Services.AddScoped<IDeviceTaggingRunner>(serviceProvider =>
    serviceProvider.GetRequiredService<DeviceTaggerFunction>());
builder.Services.AddScoped<PasswordRotationFunction>();
builder.Services.AddScoped<IPasswordRotationRunner>(serviceProvider =>
    serviceProvider.GetRequiredService<PasswordRotationFunction>());

builder.Build().Run();
