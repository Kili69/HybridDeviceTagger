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
public sealed record TaggingConfigurationSnapshot(
    IReadOnlyList<TaggingRuleOptions> Rules,
    string? ETag,
    DateTimeOffset? LastModifiedUtc,
    string Source);

/// <summary>Persists versioned tagging configuration.</summary>
public interface ITaggingConfigurationStore : ITaggingRuleProvider
{
    /// <summary>Gets the current rule snapshot and its concurrency token.</summary>
    Task<TaggingConfigurationSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken);

    /// <summary>Atomically replaces the rules if the supplied snapshot is still current.</summary>
    Task<TaggingConfigurationSnapshot> ReplaceAsync(
        IReadOnlyList<TaggingRuleOptions> rules,
        string? expectedETag,
        CancellationToken cancellationToken);
}

/// <summary>Indicates that another writer changed the tagging configuration.</summary>
public sealed class TaggingConfigurationConflictException : Exception
{
    /// <summary>Initializes the conflict exception.</summary>
    public TaggingConfigurationConflictException(Exception innerException)
        : base("The tagging configuration changed concurrently. Reload it and retry.", innerException)
    {
    }
}

/// <summary>Indicates invalid tagging-rule input.</summary>
public sealed class TaggingConfigurationValidationException : Exception
{
    /// <summary>Initializes the validation exception with all validation messages.</summary>
    public TaggingConfigurationValidationException(IReadOnlyList<string> errors)
        : base("The tagging configuration is invalid.")
    {
        Errors = errors;
    }

    /// <summary>Gets the individual validation messages.</summary>
    public IReadOnlyList<string> Errors { get; }
}