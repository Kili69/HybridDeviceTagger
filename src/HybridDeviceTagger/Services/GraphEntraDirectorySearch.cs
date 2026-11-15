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

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;

namespace HybridDeviceTagger.Services;

/// <summary>Describes a group or administrative unit offered by the rule editor.</summary>
/// <param name="Id">The Entra object ID.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="Description">The optional description.</param>
/// <param name="Assignable">Whether the tagger can manage device membership.</param>
/// <param name="Note">Why the object is not assignable, or a hint such as restricted management.</param>
public sealed record DirectoryObjectSummary(
    Guid Id,
    string DisplayName,
    string? Description,
    bool Assignable,
    string? Note);

/// <summary>Searches and resolves Entra groups and administrative units for the rule editor.</summary>
public interface IEntraDirectorySearch
{
    /// <summary>Finds security groups whose display name starts with the text.</summary>
    /// <param name="text">The display-name prefix, 1 to 64 characters.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Up to 25 groups.</returns>
    Task<IReadOnlyList<DirectoryObjectSummary>> SearchGroupsAsync(string text, CancellationToken cancellationToken);

    /// <summary>Finds administrative units whose display name starts with the text.</summary>
    /// <param name="text">The display-name prefix, 1 to 64 characters.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Up to 25 administrative units.</returns>
    Task<IReadOnlyList<DirectoryObjectSummary>> SearchAdministrativeUnitsAsync(string text, CancellationToken cancellationToken);

    /// <summary>Resolves group and administrative-unit IDs to their summaries.</summary>
    /// <param name="groupIds">The group IDs.</param>
    /// <param name="administrativeUnitIds">The administrative-unit IDs.</param>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <returns>The summaries of all objects that still exist, keyed by ID.</returns>
    Task<IReadOnlyDictionary<Guid, DirectoryObjectSummary>> ResolveAsync(
        IReadOnlyCollection<Guid> groupIds,
        IReadOnlyCollection<Guid> administrativeUnitIds,
        CancellationToken cancellationToken);
}

/// <summary>Queries Microsoft Graph with the Function identity's existing group and administrative-unit permissions.</summary>
/// <remarks>
/// Dynamic, mail-enabled, and on-premises synchronized groups and dynamic administrative units
/// are returned but marked as not assignable, because the tagger cannot change their members.
/// </remarks>
public sealed class GraphEntraDirectorySearch : IEntraDirectorySearch
{
    /// <summary>The Graph token scope.</summary>
    private static readonly TokenRequestContext TokenContext = new(["https://graph.microsoft.com/.default"]);

    /// <summary>The selected group properties.</summary>
    private const string GroupSelect = "id,displayName,description,groupTypes,mailEnabled,onPremisesSyncEnabled";

    /// <summary>The selected administrative-unit properties.</summary>
    private const string AdministrativeUnitSelect = "id,displayName,description,isMemberManagementRestricted,membershipType";

    /// <summary>The HTTP client whose base address is the Graph v1.0 endpoint.</summary>
    private readonly HttpClient _httpClient;

    /// <summary>Acquires Graph tokens for the Function identity.</summary>
    private readonly TokenCredential _credential;

    /// <summary>Initializes the search.</summary>
    /// <param name="httpClient">The HTTP client with the Graph v1.0 base address.</param>
    /// <param name="credential">Acquires Graph tokens for the Function identity.</param>
    public GraphEntraDirectorySearch(HttpClient httpClient, TokenCredential credential)
    {
        _httpClient = httpClient;
        _credential = credential;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DirectoryObjectSummary>> SearchGroupsAsync(
        string text, CancellationToken cancellationToken)
    {
        var filter = $"startswith(displayName,'{EscapeLiteral(text)}') and securityEnabled eq true";
        using var document = await GetAsync(
            $"groups?$filter={Uri.EscapeDataString(filter)}&$select={GroupSelect}&$top=25", cancellationToken);
        return [.. document!.RootElement.GetProperty("value").EnumerateArray().Select(ToGroup)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DirectoryObjectSummary>> SearchAdministrativeUnitsAsync(
        string text, CancellationToken cancellationToken)
    {
        var filter = $"startswith(displayName,'{EscapeLiteral(text)}')";
        using var document = await GetAsync(
            $"directory/administrativeUnits?$filter={Uri.EscapeDataString(filter)}&$select={AdministrativeUnitSelect}&$top=25",
            cancellationToken);
        return [.. document!.RootElement.GetProperty("value").EnumerateArray().Select(ToAdministrativeUnit)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, DirectoryObjectSummary>> ResolveAsync(
        IReadOnlyCollection<Guid> groupIds,
        IReadOnlyCollection<Guid> administrativeUnitIds,
        CancellationToken cancellationToken)
    {
        var lookups = groupIds.Distinct().Select(async id =>
            {
                using var document = await GetAsync($"groups/{id:D}?$select={GroupSelect}", cancellationToken, allowNotFound: true);
                return document is null ? null : ToGroup(document.RootElement);
            })
            .Concat(administrativeUnitIds.Distinct().Select(async id =>
            {
                using var document = await GetAsync(
                    $"directory/administrativeUnits/{id:D}?$select={AdministrativeUnitSelect}", cancellationToken, allowNotFound: true);
                return document is null ? null : ToAdministrativeUnit(document.RootElement);
            }));
        var results = await Task.WhenAll(lookups);
        return results.Where(result => result is not null)
            .GroupBy(result => result!.Id)
            .ToDictionary(group => group.Key, group => group.First()!);
    }

    /// <summary>Validates a search text and escapes it as an OData string literal.</summary>
    /// <param name="text">The search text.</param>
    /// <returns>The escaped literal content.</returns>
    /// <exception cref="ArgumentException">The text is empty or longer than 64 characters.</exception>
    internal static string EscapeLiteral(string text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > 64)
        {
            throw new ArgumentException("The search text must contain 1 to 64 characters.", nameof(text));
        }
        return trimmed.Replace("'", "''", StringComparison.Ordinal);
    }

    /// <summary>Converts a Graph group to a summary.</summary>
    /// <param name="element">The Graph group.</param>
    /// <returns>The summary.</returns>
    internal static DirectoryObjectSummary ToGroup(JsonElement element)
    {
        var dynamic = element.TryGetProperty("groupTypes", out var types) && types.ValueKind == JsonValueKind.Array &&
            types.EnumerateArray().Any(type => type.GetString() == "DynamicMembership");
        var note = dynamic ? "Dynamic membership"
            : IsTrue(element, "mailEnabled") ? "Mail-enabled"
            : IsTrue(element, "onPremisesSyncEnabled") ? "Synchronized from on-premises"
            : null;
        return new DirectoryObjectSummary(
            element.GetProperty("id").GetGuid(),
            GetString(element, "displayName") ?? string.Empty,
            GetString(element, "description"),
            note is null,
            note);
    }

    /// <summary>Converts a Graph administrative unit to a summary.</summary>
    /// <param name="element">The Graph administrative unit.</param>
    /// <returns>The summary.</returns>
    internal static DirectoryObjectSummary ToAdministrativeUnit(JsonElement element)
    {
        var dynamic = string.Equals(GetString(element, "membershipType"), "Dynamic", StringComparison.OrdinalIgnoreCase);
        return new DirectoryObjectSummary(
            element.GetProperty("id").GetGuid(),
            GetString(element, "displayName") ?? string.Empty,
            GetString(element, "description"),
            !dynamic,
            dynamic ? "Dynamic membership"
                : IsTrue(element, "isMemberManagementRestricted") ? "Restricted management (requires the scoped role)"
                : null);
    }

    /// <summary>Sends an authenticated Graph GET request.</summary>
    /// <param name="relativeUri">The URI relative to the Graph v1.0 endpoint.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <param name="allowNotFound">Returns null instead of failing for HTTP 404.</param>
    /// <returns>The response document, or null for an allowed 404.</returns>
    /// <exception cref="HttpRequestException">Graph returned another unsuccessful status code.</exception>
    private async Task<JsonDocument?> GetAsync(string relativeUri, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUri);
        var token = await _credential.GetTokenAsync(TokenContext, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
    }

    /// <summary>Reads an optional string property.</summary>
    /// <param name="element">The JSON object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The value, or null.</returns>
    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>Returns whether an optional Boolean property is true.</summary>
    /// <param name="element">The JSON object.</param>
    /// <param name="name">The property name.</param>
    /// <returns><see langword="true"/> when the property is true.</returns>
    private static bool IsTrue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
