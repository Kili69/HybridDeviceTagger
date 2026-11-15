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

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using HybridDeviceTagger.Models;

namespace HybridDeviceTagger.Services;

/// <summary>Reads and updates Microsoft Entra devices through authenticated Graph requests.</summary>
public sealed class MicrosoftGraphDeviceClient : IEntraDeviceClient, IEntraMembershipClient
{
    /// <summary>The application-permission scope used to request Microsoft Graph tokens.</summary>
    private static readonly TokenRequestContext TokenContext =
        new(["https://graph.microsoft.com/.default"]);

    /// <summary>The HTTP client whose base address is the Graph v1.0 endpoint.</summary>
    private readonly HttpClient _httpClient;

    /// <summary>The managed-identity or developer credential used to acquire Graph tokens.</summary>
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
    /// <remarks>
    /// All fifteen extension attributes are always returned, with missing values as
    /// <see langword="null"/>, so callers can compare desired and current values directly.
    /// </remarks>
    /// <exception cref="HttpRequestException">Graph returned an unsuccessful status code.</exception>
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
    /// <remarks>Only the supplied attributes are sent; Graph leaves the other attributes unchanged.</remarks>
    /// <exception cref="HttpRequestException">Graph returned an unsuccessful status code.</exception>
    public async Task SetExtensionAttributesAsync(
        string entraObjectId,
        IReadOnlyDictionary<string, string?> extensionAttributes,
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

    /// <inheritdoc />
    public Task<bool> IsGroupMemberAsync(
        Guid groupId,
        string entraObjectId,
        CancellationToken cancellationToken) =>
        IsMemberAsync($"groups/{groupId:D}/members/{entraObjectId}", cancellationToken);

    /// <inheritdoc />
    public Task AddGroupMemberAsync(
        Guid groupId,
        string entraObjectId,
        CancellationToken cancellationToken) =>
        AddMemberAsync($"groups/{groupId:D}/members/$ref", entraObjectId, cancellationToken);

    /// <inheritdoc />
    public Task RemoveGroupMemberAsync(
        Guid groupId,
        string entraObjectId,
        CancellationToken cancellationToken) =>
        RemoveMemberAsync(
            $"groups/{groupId:D}/members/{entraObjectId}/$ref",
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsAdministrativeUnitMemberAsync(
        Guid administrativeUnitId,
        string entraObjectId,
        CancellationToken cancellationToken) =>
        IsMemberAsync(
            $"directory/administrativeUnits/{administrativeUnitId:D}/members/{entraObjectId}",
            cancellationToken);

    /// <inheritdoc />
    public Task AddAdministrativeUnitMemberAsync(
        Guid administrativeUnitId,
        string entraObjectId,
        CancellationToken cancellationToken) =>
        AddMemberAsync(
            $"directory/administrativeUnits/{administrativeUnitId:D}/members/$ref",
            entraObjectId,
            cancellationToken);

    /// <inheritdoc />
    public Task RemoveAdministrativeUnitMemberAsync(
        Guid administrativeUnitId,
        string entraObjectId,
        CancellationToken cancellationToken) =>
        RemoveMemberAsync(
            $"directory/administrativeUnits/{administrativeUnitId:D}/members/{entraObjectId}/$ref",
            cancellationToken);

    /// <summary>Checks a direct membership by reading the member through its container.</summary>
    /// <param name="requestUri">The relative Graph URI of the member within the group or administrative unit.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> when Graph returns the member; <see langword="false"/> on HTTP 404.</returns>
    /// <exception cref="HttpRequestException">Graph returned another unsuccessful status code.</exception>
    private async Task<bool> IsMemberAsync(
        string requestUri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{requestUri}?$select=id");
        await AuthorizeAsync(request, cancellationToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Adds a device as a direct member through an <c>@odata.id</c> reference.</summary>
    /// <param name="requestUri">The relative Graph <c>members/$ref</c> URI.</param>
    /// <param name="entraObjectId">The Entra device object identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task representing the asynchronous change.</returns>
    /// <exception cref="HttpRequestException">Graph returned an unsuccessful status code.</exception>
    private async Task AddMemberAsync(
        string requestUri,
        string entraObjectId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(new Dictionary<string, string>
            {
                ["@odata.id"] = $"https://graph.microsoft.com/v1.0/devices/{entraObjectId}"
            })
        };
        await AuthorizeAsync(request, cancellationToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Removes a direct membership reference.</summary>
    /// <remarks>HTTP 404 is treated as success so removing an already removed membership is idempotent.</remarks>
    /// <param name="requestUri">The relative Graph <c>members/{id}/$ref</c> URI.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task representing the asynchronous change.</returns>
    /// <exception cref="HttpRequestException">Graph returned an unsuccessful status code other than 404.</exception>
    private async Task RemoveMemberAsync(
        string requestUri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, requestUri);
        await AuthorizeAsync(request, cancellationToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    /// <summary>Obtains a managed-identity token and adds it to one Graph request.</summary>
    /// <remarks>The credential caches tokens, so requesting one per call does not cause extra sign-ins.</remarks>
    /// <param name="request">The request that receives the bearer token.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task representing the asynchronous token acquisition.</returns>
    private async Task AuthorizeAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await _credential.GetTokenAsync(TokenContext, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
    }

    /// <summary>Reads an optional JSON string while treating missing and JSON null as absent.</summary>
    /// <param name="element">The JSON object to read from.</param>
    /// <param name="propertyName">The property name.</param>
    /// <returns>The string value, or <see langword="null"/>.</returns>
    private static string? GetOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind is not JsonValueKind.Null
            ? property.GetString()
            : null;
}