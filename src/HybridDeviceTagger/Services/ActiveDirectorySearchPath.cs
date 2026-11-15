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

using System.Text.RegularExpressions;
using System.Text;

namespace HybridDeviceTagger.Services;

/// <summary>
/// Parses Active Directory search paths and resolves forest-relative OU paths against domain
/// naming contexts.
/// </summary>
/// <remarks>
/// A path without a <c>DC=</c> component is treated as forest-relative and is searched in every
/// forest domain. A path containing domain components is already domain-specific.
/// </remarks>
internal static partial class ActiveDirectorySearchPath
{
    /// <summary>The maximum accepted path length, limiting parser work on untrusted input.</summary>
    internal const int MaximumDistinguishedNameLength = 2048;

    /// <summary>Determines whether a path omits domain components and must be resolved forest-wide.</summary>
    /// <param name="path">The OU path or complete distinguished name.</param>
    /// <returns><see langword="true"/> when no unescaped <c>DC=</c> component is present.</returns>
    internal static bool IsForestRelative(string path) =>
        !DomainComponent().IsMatch(path);

    /// <summary>Determines whether every component in a relative path is an OU component.</summary>
    /// <param name="path">The forest-relative path to validate.</param>
    /// <returns><see langword="true"/> when the path contains only <c>OU=</c> components.</returns>
    internal static bool IsOuPath(string path) =>
        TryValidateBaseDn(path, out var hasDomainComponent) && !hasDomainComponent;

    /// <summary>Determines whether a search base is an OU path with an optional domain suffix.</summary>
    /// <param name="path">The forest-relative OU path or domain-specific distinguished name.</param>
    /// <returns><see langword="true"/> when all components and escapes are valid.</returns>
    internal static bool IsValidBaseDn(string path) =>
        TryValidateBaseDn(path, out _);

    /// <summary>Builds a domain-specific distinguished name from a relative OU path.</summary>
    /// <param name="ouPath">An OU-only path such as <c>OU=Tier0,OU=Admin</c>.</param>
    /// <param name="domainNamingContext">A naming context such as <c>DC=contoso,DC=com</c>.</param>
    /// <returns>The complete distinguished name used as an LDAP search base.</returns>
    internal static string CombineWithDomain(string ouPath, string domainNamingContext) =>
        $"{ouPath.Trim().TrimEnd(',')},{domainNamingContext.Trim()}";

    /// <summary>Converts the ordered domain components in a distinguished name to a DNS name.</summary>
    /// <param name="distinguishedName">A domain-specific distinguished name.</param>
    /// <returns>The DNS domain name, for example <c>contoso.com</c>.</returns>
    internal static string GetDomainDnsName(string distinguishedName) =>
        string.Join(
            '.',
            DomainComponentValue().Matches(distinguishedName)
                .Select(match => match.Groups[1].Value.Trim()));

    /// <summary>Validates that a path consists of OU components optionally followed by DC components.</summary>
    /// <remarks>
    /// Only <c>OU</c> and <c>DC</c> component types are accepted, and an <c>OU</c> after a <c>DC</c> is
    /// rejected, so a search base can never point at containers such as <c>CN=Users</c>.
    /// </remarks>
    /// <param name="path">The path to validate.</param>
    /// <param name="hasDomainComponent">Receives whether at least one <c>DC=</c> component was found.</param>
    /// <returns><see langword="true"/> when the path is valid.</returns>
    private static bool TryValidateBaseDn(string path, out bool hasDomainComponent)
    {
        hasDomainComponent = false;
        if (!TrySplitComponents(path, out var components))
        {
            return false;
        }

        foreach (var component in components)
        {
            var separatorIndex = component.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex == component.Length - 1)
            {
                return false;
            }

            var componentType = component[..separatorIndex].Trim();
            var componentValue = component[(separatorIndex + 1)..];
            if (!IsValidComponentValue(componentValue))
            {
                return false;
            }

            if (componentType.Equals("DC", StringComparison.OrdinalIgnoreCase))
            {
                hasDomainComponent = true;
            }
            else if (!componentType.Equals("OU", StringComparison.OrdinalIgnoreCase) ||
                     hasDomainComponent)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Splits a distinguished name at unescaped commas.</summary>
    /// <param name="path">The path to split.</param>
    /// <param name="components">Receives the trimmed, still-escaped components on success.</param>
    /// <returns>
    /// <see langword="false"/> when the path is empty, too long, contains control characters, has an
    /// empty component, or ends with a dangling escape.
    /// </returns>
    private static bool TrySplitComponents(string path, out string[] components)
    {
        components = [];
        if (string.IsNullOrWhiteSpace(path) ||
            path.Length > MaximumDistinguishedNameLength ||
            path.Any(char.IsControl))
        {
            return false;
        }

        var result = new List<string>();
        var component = new StringBuilder();
        var escaped = false;
        foreach (var character in path.Trim())
        {
            if (escaped)
            {
                component.Append(character);
                escaped = false;
                continue;
            }

            if (character == '\\')
            {
                component.Append(character);
                escaped = true;
                continue;
            }

            if (character == ',')
            {
                if (!AddComponent(result, component))
                {
                    return false;
                }
                continue;
            }

            component.Append(character);
        }

        if (escaped || !AddComponent(result, component))
        {
            return false;
        }

        components = [.. result];
        return true;
    }

    /// <summary>Appends the trimmed buffered component and clears the buffer.</summary>
    /// <param name="components">The list that receives the component.</param>
    /// <param name="component">The buffer holding the current component.</param>
    /// <returns><see langword="false"/> when the component is empty.</returns>
    private static bool AddComponent(List<string> components, StringBuilder component)
    {
        var value = component.ToString().Trim();
        component.Clear();
        if (value.Length == 0)
        {
            return false;
        }

        components.Add(value);
        return true;
    }

    /// <summary>Validates an attribute value against RFC 4514 escaping rules.</summary>
    /// <param name="value">The raw value after the <c>=</c> separator.</param>
    /// <returns>
    /// <see langword="true"/> when the value is non-empty, has no control characters, no leading space
    /// or <c>#</c>, no trailing space, no unescaped special characters, and no dangling escape.
    /// </returns>
    private static bool IsValidComponentValue(string value)
    {
        if (string.IsNullOrEmpty(value) ||
            value.Any(char.IsControl) ||
            value[0] is ' ' or '#' ||
            value[^1] == ' ')
        {
            return false;
        }

        var escaped = false;
        foreach (var character in value)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (character == '\\')
            {
                escaped = true;
            }
            else if (character is ',' or '+' or '"' or '<' or '>' or ';')
            {
                return false;
            }
        }

        return !escaped;
    }

    /// <summary>Matches an unescaped domain component marker.</summary>
    /// <returns>The source-generated regular expression.</returns>
    [GeneratedRegex(@"(?:^|(?<!\\),)\s*DC\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex DomainComponent();

    /// <summary>Captures values from unescaped domain components in their original order.</summary>
    /// <returns>The source-generated regular expression.</returns>
    [GeneratedRegex(@"(?:^|(?<!\\),)\s*DC\s*=\s*([^,]+)", RegexOptions.IgnoreCase)]
    private static partial Regex DomainComponentValue();

}