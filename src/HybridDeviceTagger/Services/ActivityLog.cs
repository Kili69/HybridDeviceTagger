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

using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using HybridDeviceTagger.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HybridDeviceTagger.Services;

/// <summary>Defines the activity-log categories.</summary>
public static class ActivityCategories
{
    /// <summary>Changes to rules, the forest domain, trust anchors, or the service account.</summary>
    public const string Configuration = "Configuration";

    /// <summary>Device-tagging runs and individual device changes.</summary>
    public const string Tagging = "Tagging";

    /// <summary>Credential initialization, password rotation, and LDAP validation.</summary>
    public const string Password = "Password";

    /// <summary>Portal sign-ins and denied management requests.</summary>
    public const string Access = "Access";

    /// <summary>All categories accepted by activity queries.</summary>
    public static readonly IReadOnlyList<string> All = [Configuration, Tagging, Password, Access];
}

/// <summary>Defines the activity-log outcomes.</summary>
public static class ActivityOutcomes
{
    /// <summary>The activity completed successfully.</summary>
    public const string Succeeded = "Succeeded";

    /// <summary>The activity was attempted and failed.</summary>
    public const string Failed = "Failed";

    /// <summary>The caller was not authorized.</summary>
    public const string Denied = "Denied";

    /// <summary>All outcomes accepted by activity queries.</summary>
    public static readonly IReadOnlyList<string> All = [Succeeded, Failed, Denied];
}

/// <summary>Describes one auditable activity.</summary>
/// <remarks>Entries must never contain passwords, tokens, or other secrets.</remarks>
/// <param name="Category">One of <see cref="ActivityCategories"/>.</param>
/// <param name="Action">The short action name, for example <c>RuleUpserted</c>.</param>
/// <param name="Outcome">One of <see cref="ActivityOutcomes"/>.</param>
/// <param name="Actor">The user or system component that performed the activity.</param>
/// <param name="Target">The affected object, for example a rule name or device ID.</param>
/// <param name="Details">Additional non-secret details.</param>
public sealed record ActivityEntry(
    string Category,
    string Action,
    string Outcome,
    string Actor,
    string? Target = null,
    string? Details = null);

/// <summary>Records auditable activities.</summary>
public interface IActivityLog
{
    /// <summary>Records one activity without throwing.</summary>
    /// <param name="entry">The non-secret activity entry.</param>
    void Record(ActivityEntry entry);
}

/// <summary>Writes activities as structured log records that flow into Log Analytics.</summary>
/// <remarks>
/// The dedicated logger category and <c>Activity*</c> properties let the activity query select
/// these records from <c>AppTraces</c> without parsing message text.
/// </remarks>
public sealed class LoggerActivityLog : IActivityLog
{
    /// <summary>The logger category that identifies activity records.</summary>
    public const string LoggerCategory = "HybridDeviceTagger.Activity";

    /// <summary>The maximum length of each free-text property.</summary>
    private const int MaximumTextLength = 2000;

    /// <summary>The activity logger.</summary>
    private readonly ILogger _logger;

    /// <summary>Initializes the activity log.</summary>
    /// <param name="loggerFactory">Creates the dedicated activity logger.</param>
    public LoggerActivityLog(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger(LoggerCategory);
    }

    /// <inheritdoc />
    public void Record(ActivityEntry entry)
    {
        try
        {
            _logger.Log(
                entry.Outcome == ActivityOutcomes.Succeeded ? LogLevel.Information : LogLevel.Warning,
                "Activity {ActivityCategory}/{ActivityAction} {ActivityOutcome} by {ActivityActor} on {ActivityTarget}: {ActivityDetails}",
                entry.Category,
                entry.Action,
                entry.Outcome,
                Truncate(entry.Actor),
                Truncate(entry.Target) ?? string.Empty,
                Truncate(entry.Details) ?? string.Empty);
        }
        catch (Exception)
        {
            // Auditing must never break the audited operation.
        }
    }

    /// <summary>Limits free text so a single record cannot grow unbounded.</summary>
    /// <param name="value">The text to limit.</param>
    /// <returns>The original or truncated text.</returns>
    private static string? Truncate(string? value) =>
        value is { Length: > MaximumTextLength } ? value[..MaximumTextLength] + "…" : value;
}

/// <summary>Discards activities; used where no activity log is configured, for example in tests.</summary>
public sealed class NullActivityLog : IActivityLog
{
    /// <summary>The shared instance.</summary>
    public static readonly NullActivityLog Instance = new();

    /// <inheritdoc />
    public void Record(ActivityEntry entry)
    {
    }
}

/// <summary>Represents one activity returned by a query.</summary>
/// <param name="TimestampUtc">When the activity was recorded.</param>
/// <param name="Category">The activity category.</param>
/// <param name="Action">The action name.</param>
/// <param name="Outcome">The outcome.</param>
/// <param name="Actor">The user or system component.</param>
/// <param name="Target">The affected object.</param>
/// <param name="Details">Additional non-secret details.</param>
public sealed record ActivityRecord(
    DateTimeOffset TimestampUtc,
    string Category,
    string Action,
    string Outcome,
    string Actor,
    string Target,
    string Details);

/// <summary>Selects activities to read.</summary>
/// <param name="Hours">The look-back window in hours, 1 to 720.</param>
/// <param name="Categories">The categories to include; empty includes all.</param>
/// <param name="Outcomes">The outcomes to include; empty includes all.</param>
/// <param name="Take">The maximum number of records, 1 to 1000.</param>
public sealed record ActivityQuery(
    int Hours,
    IReadOnlyCollection<string> Categories,
    IReadOnlyCollection<string> Outcomes,
    int Take);

/// <summary>Reads recorded activities.</summary>
public interface IActivityLogReader
{
    /// <summary>Gets whether a query source is configured.</summary>
    bool IsConfigured { get; }

    /// <summary>Reads the newest matching activities first.</summary>
    /// <param name="query">The validated query.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching activities.</returns>
    Task<IReadOnlyList<ActivityRecord>> QueryAsync(ActivityQuery query, CancellationToken cancellationToken);
}

/// <summary>Queries the activity log from the Application Insights Log Analytics workspace.</summary>
/// <remarks>
/// Uses a resource-centric query against the Application Insights component, so the Function
/// identity needs only read access on that component. Log ingestion typically delays new
/// records by a few minutes.
/// </remarks>
public sealed class LogAnalyticsActivityLogReader : IActivityLogReader
{
    /// <summary>The Log Analytics query token scope.</summary>
    private const string QueryScope = "https://api.loganalytics.io/.default";

    /// <summary>Sends query requests.</summary>
    private readonly HttpClient _httpClient;

    /// <summary>Acquires tokens for the Function identity.</summary>
    private readonly TokenCredential _credential;

    /// <summary>The Application Insights resource ID, or null when not configured.</summary>
    private readonly string? _resourceId;

    /// <summary>Initializes the reader.</summary>
    /// <param name="httpClient">Sends query requests.</param>
    /// <param name="credential">Acquires tokens for the Function identity.</param>
    /// <param name="options">Provides the Application Insights resource ID.</param>
    public LogAnalyticsActivityLogReader(
        HttpClient httpClient,
        TokenCredential credential,
        IOptions<MonitoringOptions> options)
    {
        _httpClient = httpClient;
        _credential = credential;
        _resourceId = string.IsNullOrWhiteSpace(options.Value.ApplicationInsightsResourceId)
            ? null
            : options.Value.ApplicationInsightsResourceId.TrimEnd('/');
    }

    /// <inheritdoc />
    public bool IsConfigured => _resourceId is not null;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">No Application Insights resource is configured.</exception>
    /// <exception cref="HttpRequestException">The query service returned an unsuccessful status code.</exception>
    public async Task<IReadOnlyList<ActivityRecord>> QueryAsync(
        ActivityQuery query,
        CancellationToken cancellationToken)
    {
        if (_resourceId is null)
        {
            throw new InvalidOperationException("No Application Insights resource is configured for activity queries.");
        }

        var token = await _credential.GetTokenAsync(new TokenRequestContext([QueryScope]), cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.loganalytics.io/v1{_resourceId}/query")
        {
            Content = JsonContent.Create(new
            {
                query = BuildQuery(query),
                timespan = $"PT{query.Hours}H"
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"The activity query failed with HTTP {(int)response.StatusCode}: {body}",
                null,
                response.StatusCode);
        }

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
        return ParseRecords(document.RootElement);
    }

    /// <summary>Builds the KQL query from allow-listed values only.</summary>
    /// <param name="query">The validated query.</param>
    /// <returns>The KQL text.</returns>
    internal static string BuildQuery(ActivityQuery query)
    {
        var categories = query.Categories.Where(ActivityCategories.All.Contains).ToArray();
        var outcomes = query.Outcomes.Where(ActivityOutcomes.All.Contains).ToArray();
        // Resource-centric queries on an Application Insights component use the classic schema.
        var lines = new List<string>
        {
            "traces",
            $"| where timestamp > ago({query.Hours}h)",
            $"| where tostring(customDimensions.CategoryName) == '{LoggerActivityLog.LoggerCategory}'",
            "| extend Category = tostring(customDimensions.ActivityCategory), Action = tostring(customDimensions.ActivityAction), " +
            "Outcome = tostring(customDimensions.ActivityOutcome), Actor = tostring(customDimensions.ActivityActor), " +
            "Target = tostring(customDimensions.ActivityTarget), Details = tostring(customDimensions.ActivityDetails)"
        };
        if (categories.Length > 0)
        {
            lines.Add($"| where Category in ({string.Join(", ", categories.Select(value => $"'{value}'"))})");
        }
        if (outcomes.Length > 0)
        {
            lines.Add($"| where Outcome in ({string.Join(", ", outcomes.Select(value => $"'{value}'"))})");
        }
        lines.Add("| project TimeGenerated = timestamp, Category, Action, Outcome, Actor, Target, Details");
        lines.Add("| order by TimeGenerated desc");
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"| take {query.Take}"));
        return string.Join('\n', lines);
    }

    /// <summary>Converts the first result table into activity records.</summary>
    /// <param name="root">The query response document.</param>
    /// <returns>The activity records.</returns>
    /// <exception cref="JsonException">The response contains no result table.</exception>
    internal static IReadOnlyList<ActivityRecord> ParseRecords(JsonElement root)
    {
        if (!root.TryGetProperty("tables", out var tables) || tables.GetArrayLength() == 0)
        {
            throw new JsonException("The activity query returned no result table.");
        }

        var table = tables[0];
        var columns = table.GetProperty("columns").EnumerateArray()
            .Select((column, index) => (Name: column.GetProperty("name").GetString() ?? string.Empty, index))
            .ToDictionary(column => column.Name, column => column.index, StringComparer.OrdinalIgnoreCase);
        var records = new List<ActivityRecord>();
        foreach (var row in table.GetProperty("rows").EnumerateArray())
        {
            string Read(string name) =>
                columns.TryGetValue(name, out var index) && row[index].ValueKind == JsonValueKind.String
                    ? row[index].GetString() ?? string.Empty
                    : string.Empty;
            records.Add(new ActivityRecord(
                DateTimeOffset.Parse(Read("TimeGenerated"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
                Read("Category"),
                Read("Action"),
                Read("Outcome"),
                Read("Actor"),
                Read("Target"),
                Read("Details")));
        }
        return records;
    }
}
