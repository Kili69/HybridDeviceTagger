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

using HybridDeviceTagger.Options;

namespace HybridDeviceTagger.Services;

/// <summary>Describes one versioned tagging-rule snapshot.</summary>
/// <param name="Rules">The normalized, validated rules.</param>
/// <param name="ETag">The concurrency token, or <see langword="null"/> when the rules come from app settings.</param>
/// <param name="LastModifiedUtc">The time the stored rules were last changed, if stored.</param>
/// <param name="Source">The rule origin: <c>blob</c> for stored rules or <c>appSetting</c> for the fallback.</param>
public sealed record TaggingConfigurationSnapshot(
    IReadOnlyList<TaggingRuleOptions> Rules,
    string? ETag,
    DateTimeOffset? LastModifiedUtc,
    string Source);

/// <summary>Persists versioned tagging configuration.</summary>
public interface ITaggingConfigurationStore : ITaggingRuleProvider
{
    /// <summary>Gets the current rule snapshot and its concurrency token.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The current snapshot.</returns>
    Task<TaggingConfigurationSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken);

    /// <summary>Atomically replaces the rules if the supplied snapshot is still current.</summary>
    /// <param name="rules">The complete new rule set.</param>
    /// <param name="expectedETag">
    /// The ETag of the snapshot the change is based on, or <see langword="null"/> when no stored
    /// configuration is expected to exist yet.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored snapshot with its new ETag.</returns>
    /// <exception cref="TaggingConfigurationValidationException">The rules are invalid.</exception>
    /// <exception cref="TaggingConfigurationConflictException">The stored configuration changed concurrently.</exception>
    Task<TaggingConfigurationSnapshot> ReplaceAsync(
        IReadOnlyList<TaggingRuleOptions> rules,
        string? expectedETag,
        CancellationToken cancellationToken);
}

/// <summary>Indicates that another writer changed the tagging configuration.</summary>
public sealed class TaggingConfigurationConflictException : Exception
{
    /// <summary>Initializes the conflict exception.</summary>
    /// <param name="innerException">The underlying storage precondition failure.</param>
    public TaggingConfigurationConflictException(Exception innerException)
        : base("The tagging configuration changed concurrently. Reload it and retry.", innerException)
    {
    }
}

/// <summary>Indicates invalid tagging-rule input.</summary>
public sealed class TaggingConfigurationValidationException : Exception
{
    /// <summary>Initializes the validation exception with all validation messages.</summary>
    /// <param name="errors">The individual validation messages returned to the caller.</param>
    public TaggingConfigurationValidationException(IReadOnlyList<string> errors)
        : base("The tagging configuration is invalid.")
    {
        Errors = errors;
    }

    /// <summary>Gets the individual validation messages.</summary>
    public IReadOnlyList<string> Errors { get; }
}