/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
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