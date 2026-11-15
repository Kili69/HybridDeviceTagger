/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * Copyright 2026 Andreas Lucas
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 * SPDX-License-Identifier: Apache-2.0
 */

// Composition root of the HybridDeviceTagger Azure Functions app (.NET 8 isolated worker, Functions v4).
//
// These top-level statements configure the host and its dependency-injection container:
// - OpenTelemetry with the optional Azure Monitor exporter for Application Insights.
// - Strongly typed options (Active Directory, tagging, Key Vault, password rotation, management API)
//   that are validated at startup so misconfiguration stops the host instead of causing partial runs.
// - DefaultAzureCredential as the single TokenCredential for Key Vault, Microsoft Graph, Blob, and
//   Queue access; no Azure credential is stored in configuration.
// - Active Directory (LDAP), Microsoft Graph, and Blob/Queue-backed services behind interfaces so the
//   orchestration services and Functions stay independent of their infrastructure and testable.
// Function classes are discovered by the Functions SDK and resolve their dependencies from this container.

using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Queues;
using HybridDeviceTagger;
using HybridDeviceTagger.Options;
using HybridDeviceTagger.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using DnsClient;
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
    // Application Insights accepts only Entra-authenticated ingestion, so the connection string
    // alone cannot be used to forge telemetry such as activity-log entries.
    telemetry.UseAzureMonitorExporter(options =>
    {
        if (string.Equals(builder.Configuration["APPLICATIONINSIGHTS_AUTHENTICATION_STRING"], "Authorization=AAD", StringComparison.OrdinalIgnoreCase))
        {
            options.Credential = new DefaultAzureCredential();
        }
    });
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

builder.Services
    .AddOptions<ManagementApiOptions>()
    .BindConfiguration(ManagementApiOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<ManagementAuthorizationOptions>()
    .BindConfiguration(ManagementAuthorizationOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<MonitoringOptions>()
    .BindConfiguration(MonitoringOptions.SectionName)
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
builder.Services.AddSingleton<ILookupClient>(_ => new LookupClient());
builder.Services.AddSingleton<IActiveDirectoryServerResolver, ActiveDirectoryServerResolver>();
builder.Services.AddSingleton<ActiveDirectoryLdapConnectionFactory>();
builder.Services.AddSingleton<LdapActiveDirectoryDeviceSource>();
builder.Services.AddSingleton<IActiveDirectoryDeviceSource>(serviceProvider =>
    serviceProvider.GetRequiredService<LdapActiveDirectoryDeviceSource>());
builder.Services.AddSingleton<IActiveDirectoryRuleValidator>(serviceProvider =>
    serviceProvider.GetRequiredService<LdapActiveDirectoryDeviceSource>());
builder.Services.AddSingleton<IActiveDirectoryPasswordClient, LdapActiveDirectoryPasswordClient>();
builder.Services.AddSingleton<IActiveDirectoryCredentialInitializer, ActiveDirectoryCredentialInitializer>();
builder.Services.AddSingleton<IPasswordGenerator, SecurePasswordGenerator>();
builder.Services.AddTransient<PasswordRotationService>();
// A singleton keeps the short-lived Owner/Contributor lookup cache across requests.
builder.Services.AddHttpClient(nameof(ManagementApiAuthorizer));
builder.Services.AddSingleton<IManagementApiAuthorizer>(serviceProvider =>
    new ManagementApiAuthorizer(
        serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ManagementApiAuthorizer)),
        serviceProvider.GetRequiredService<TokenCredential>(),
        serviceProvider.GetRequiredService<
            Microsoft.Extensions.Options.IOptions<ManagementAuthorizationOptions>>()));
builder.Services.AddSingleton<IActivityLog, LoggerActivityLog>();
builder.Services.AddHttpClient<IActivityLogReader, LogAnalyticsActivityLogReader>();
builder.Services.AddSingleton<IActiveDirectoryAccountService, ActiveDirectoryAccountService>();
builder.Services.AddScoped<IEntraDirectorySearch>(serviceProvider =>
    new GraphEntraDirectorySearch(
        serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("MicrosoftGraph"),
        serviceProvider.GetRequiredService<TokenCredential>()));
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
builder.Services.AddSingleton<IOperationLock>(serviceProvider =>
    new BlobOperationLock(
        serviceProvider.GetRequiredService<BlobServiceClient>()
            .GetBlobContainerClient("run-traces"),
        serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BlobOperationLock>>()));
builder.Services.AddSingleton(serviceProvider =>
{
    // The Functions queue trigger expects Base64-encoded messages by default.
    var options = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };
    var connectionString = builder.Configuration["AzureWebJobsStorage"];
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        return new QueueServiceClient(connectionString, options);
    }

    var accountName = builder.Configuration["AzureWebJobsStorage:accountName"];
    if (string.IsNullOrWhiteSpace(accountName))
    {
        throw new InvalidOperationException(
            "AzureWebJobsStorage or AzureWebJobsStorage__accountName must be configured.");
    }

    return new QueueServiceClient(
        new Uri($"https://{accountName}.queue.core.windows.net"),
        serviceProvider.GetRequiredService<TokenCredential>(),
        options);
});
builder.Services.AddSingleton<IOperationRequestQueue, StorageOperationRequestQueue>();
builder.Services.AddSingleton<OptionsTaggingRuleProvider>();
builder.Services.AddSingleton<IActiveDirectoryConfigurationStore>(serviceProvider =>
    new BlobActiveDirectoryConfigurationStore(
        serviceProvider.GetRequiredService<BlobServiceClient>()
            .GetBlobContainerClient("run-traces")
            .GetBlobClient(BlobActiveDirectoryConfigurationStore.BlobName),
        serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ActiveDirectoryOptions>>()));
builder.Services.AddSingleton<ITrustedCertificateStore>(serviceProvider =>
    new BlobTrustedCertificateStore(
        serviceProvider.GetRequiredService<BlobServiceClient>()
            .GetBlobContainerClient("run-traces")
            .GetBlobClient(BlobTrustedCertificateStore.BlobName),
        serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ActiveDirectoryOptions>>(),
        serviceProvider.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<IOperationScheduleStore>(serviceProvider =>
    new BlobOperationScheduleStore(
        serviceProvider.GetRequiredService<BlobServiceClient>()
            .GetBlobContainerClient("run-traces")
            .GetBlobClient(BlobOperationScheduleStore.BlobName),
        serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TaggingOptions>>(),
        serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PasswordRotationOptions>>()));
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

builder.Build().Run();
