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
    /// <param name="domain">The Active Directory DNS domain name.</param>
    /// <param name="cancellationToken">Cancels the DNS queries.</param>
    /// <returns>Distinct domain-controller host names in RFC 2782 priority and weight order.</returns>
    /// <exception cref="ActiveDirectoryLdapException">DNS returned no domain controllers.</exception>
    Task<IReadOnlyList<string>> ResolveAsync(
        string domain,
        CancellationToken cancellationToken);
}

/// <summary>Discovers domain controllers from the <c>_ldap._tcp</c> DNS SRV records of a domain.</summary>
internal sealed class ActiveDirectoryServerResolver : IActiveDirectoryServerResolver
{
    /// <summary>Performs the DNS SRV queries.</summary>
    private readonly ILookupClient _lookupClient;

    /// <summary>Writes discovery diagnostics.</summary>
    private readonly ILogger<ActiveDirectoryServerResolver> _logger;

    /// <summary>Initializes a new instance of the <see cref="ActiveDirectoryServerResolver"/> class.</summary>
    /// <param name="lookupClient">Performs the DNS SRV queries.</param>
    /// <param name="logger">Writes discovery diagnostics.</param>
    public ActiveDirectoryServerResolver(
        ILookupClient lookupClient,
        ILogger<ActiveDirectoryServerResolver> logger)
    {
        _lookupClient = lookupClient;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Queries the domain-controller locator record <c>_ldap._tcp.dc._msdcs.&lt;domain&gt;</c> first
    /// and falls back to the generic <c>_ldap._tcp.&lt;domain&gt;</c> record when it is empty.
    /// </remarks>
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

    /// <summary>Queries one DNS SRV record name.</summary>
    /// <param name="query">The SRV record name.</param>
    /// <param name="cancellationToken">Cancels the DNS query.</param>
    /// <returns>The SRV records in the answer section.</returns>
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

    /// <summary>Orders SRV targets by ascending priority and weighted random selection within a priority.</summary>
    /// <remarks>
    /// Implements the RFC 2782 selection algorithm so clients spread load across domain controllers
    /// of equal priority; targets with zero total weight are shuffled uniformly.
    /// </remarks>
    /// <param name="records">The SRV targets to order.</param>
    /// <returns>All targets in connection-attempt order.</returns>
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

    /// <summary>Selects the target whose cumulative weight range contains the random selection.</summary>
    /// <param name="records">The remaining targets of one priority.</param>
    /// <param name="selection">A random number between 1 and the total weight, inclusive.</param>
    /// <returns>The index of the selected target.</returns>
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

/// <summary>Represents one domain-controller target from a DNS SRV record.</summary>
/// <param name="Host">The target host name without a trailing dot.</param>
/// <param name="Priority">The SRV priority; lower values are tried first.</param>
/// <param name="Weight">The relative SRV weight within the same priority.</param>
internal sealed record ActiveDirectorySrvTarget(string Host, ushort Priority, ushort Weight);
