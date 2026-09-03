using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using HybridDeviceTagger.Models;

namespace HybridDeviceTagger.Services;

/// <summary>Reads and updates Microsoft Entra devices through authenticated Graph requests.</summary>
public sealed class MicrosoftGraphDeviceClient : IEntraDeviceClient
{
    private static readonly TokenRequestContext TokenContext =
        new(["https://graph.microsoft.com/.default"]);

    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;

    /// <summary>Initializes a new instance of the <see cref="MicrosoftGraphDeviceClient"/> class.</summary>
    /// <param name="httpClient">The HTTP client configured with the Microsoft Graph base address.</param>
    /// <param name="credential">The credential used to request a Graph access token.</param>
    public MicrosoftGraphDeviceClient(HttpClient httpClient, TokenCredential credential)
    {
        _httpClient = httpClient;
        _credential = credential;
    }

    /// <inheritdoc />
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
        var extensionAttributes = Enumerable.Range(1, 15)
            .ToDictionary(
                index => $"extensionAttribute{index}",
                index => item.TryGetProperty("extensionAttributes", out var attributes)
                    ? GetOptionalString(attributes, $"extensionAttribute{index}")
                    : null,
                StringComparer.OrdinalIgnoreCase);
        return new EntraDevice(
            item.GetProperty("id").GetString()!,
            item.GetProperty("deviceId").GetGuid(),
            GetOptionalString(item, "displayName"),
            GetOptionalString(item, "trustType"),
            extensionAttributes);
    }

    /// <inheritdoc />
    public async Task SetExtensionAttributesAsync(
        string entraObjectId,
        IReadOnlyDictionary<string, string> extensionAttributes,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"devices/{entraObjectId}")
        {
            Content = JsonContent.Create(new
            {
                extensionAttributes
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