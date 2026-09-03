using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using HybridDeviceTagger.Models;

namespace HybridDeviceTagger.Services;

public sealed class MicrosoftGraphDeviceClient : IEntraDeviceClient
{
    private static readonly TokenRequestContext TokenContext =
        new(["https://graph.microsoft.com/.default"]);

    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;

    public MicrosoftGraphDeviceClient(HttpClient httpClient, TokenCredential credential)
    {
        _httpClient = httpClient;
        _credential = credential;
    }

    public async Task<EntraDevice?> FindByDeviceIdAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var filter = Uri.EscapeDataString($"deviceId eq '{deviceId:D}'");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"devices?$filter={filter}&$select=id,deviceId,displayName,trustType,extensionAttributes");
        await AuthorizeAsync(request, cancellationToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var values = document.RootElement.GetProperty("value");
        if (values.GetArrayLength() == 0)
        {
            return null;
        }

        var item = values[0];
        return new EntraDevice(
            item.GetProperty("id").GetString()!,
            item.GetProperty("deviceId").GetGuid(),
            GetOptionalString(item, "displayName"),
            GetOptionalString(item, "trustType"),
            item.TryGetProperty("extensionAttributes", out var attributes)
                ? GetOptionalString(attributes, "extensionAttribute1")
                : null);
    }

    public async Task SetExtensionAttribute1Async(
        string entraObjectId,
        string value,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"devices/{entraObjectId}")
        {
            Content = JsonContent.Create(new
            {
                extensionAttributes = new
                {
                    extensionAttribute1 = value
                }
            })
        };
        await AuthorizeAsync(request, cancellationToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task AuthorizeAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await _credential.GetTokenAsync(TokenContext, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
    }

    private static string? GetOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind is not JsonValueKind.Null
            ? property.GetString()
            : null;
}