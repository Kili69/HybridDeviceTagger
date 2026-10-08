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

using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Logging;

namespace HybridDeviceTagger.Services;

/// <summary>Resolves Active Directory domain controllers through DNS SRV records.</summary>
public interface IActiveDirectoryServerResolver
{
    /// <summary>Returns ordered domain-controller DNS names for an Active Directory domain.</summary>
    Task<IReadOnlyList<string>> ResolveAsync(
        string domain,
        CancellationToken cancellationToken);
}

internal sealed class ActiveDirectoryServerResolver : IActiveDirectoryServerResolver
{
    private readonly ILookupClient _lookupClient;
    private readonly ILogger<ActiveDirectoryServerResolver> _logger;

    public ActiveDirectoryServerResolver(
        ILookupClient lookupClient,
        ILogger<ActiveDirectoryServerResolver> logger)
    {
        _lookupClient = lookupClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> ResolveAsync(
        string domain,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        var normalizedDomain = domain.Trim().TrimEnd('.');
        var records = await QueryAsync(
            $"_ldap._tcp.dc._msdcs.{normalizedDomain}",
            cancellationToken);
        if (records.Count == 0)
        {
            records = await QueryAsync($"_ldap._tcp.{normalizedDomain}", cancellationToken);
        }
        if (records.Count == 0)
        {
            throw new ActiveDirectoryLdapException(
                $"Active Directory DNS discovery returned no domain controllers for '{normalizedDomain}'.",
                81,
                new InvalidOperationException("The Active Directory DNS SRV response was empty."));
        }

        var servers = OrderByPriorityAndWeight(records.Select(record =>
                new ActiveDirectorySrvTarget(
                    record.Target.Value.TrimEnd('.'),
                    record.Priority,
                    record.Weight)))
            .Select(record => record.Host)
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _logger.LogInformation(
            "Discovered {ServerCount} Active Directory domain controllers for {Domain} through DNS SRV",
            servers.Length,
            normalizedDomain);
        return servers;
    }

    private async Task<IReadOnlyList<SrvRecord>> QueryAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var response = await _lookupClient.QueryAsync(
            query,
            QueryType.SRV,
            QueryClass.IN,
            cancellationToken);
        return response.Answers.SrvRecords().ToArray();
    }

    internal static IReadOnlyList<ActiveDirectorySrvTarget> OrderByPriorityAndWeight(
        IEnumerable<ActiveDirectorySrvTarget> records)
    {
        var remainingRecords = records.ToArray();
        var ordered = new List<ActiveDirectorySrvTarget>(remainingRecords.Length);
        foreach (var priorityGroup in remainingRecords
            .GroupBy(record => record.Priority)
            .OrderBy(group => group.Key))
        {
            var remaining = priorityGroup.ToList();
            while (remaining.Count > 0)
            {
                var totalWeight = remaining.Sum(record => (int)record.Weight);
                var selectedIndex = totalWeight == 0
                    ? Random.Shared.Next(remaining.Count)
                    : SelectWeightedIndex(remaining, Random.Shared.Next(1, totalWeight + 1));
                ordered.Add(remaining[selectedIndex]);
                remaining.RemoveAt(selectedIndex);
            }
        }
        return ordered;
    }

    private static int SelectWeightedIndex(
        IReadOnlyList<ActiveDirectorySrvTarget> records,
        int selection)
    {
        var cumulativeWeight = 0;
        for (var index = 0; index < records.Count; index++)
        {
            cumulativeWeight += records[index].Weight;
            if (selection <= cumulativeWeight)
            {
                return index;
            }
        }
        return records.Count - 1;
    }
}

internal sealed record ActiveDirectorySrvTarget(string Host, ushort Priority, ushort Weight);
