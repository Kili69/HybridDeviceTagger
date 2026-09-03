using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Azure.Security.KeyVault.Secrets;
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

builder.Services.AddOpenTelemetry()
    .UseFunctionsWorkerDefaults()
    .UseAzureMonitorExporter();

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
builder.Services.AddHttpClient<IEntraDeviceClient, MicrosoftGraphDeviceClient>(client =>
{
    client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
});
builder.Services.AddTransient<DeviceTaggingService>();

builder.Build().Run();
