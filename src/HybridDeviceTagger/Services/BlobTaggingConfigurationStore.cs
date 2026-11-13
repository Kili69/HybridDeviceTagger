/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HybridDeviceTagger.Options;

namespace HybridDeviceTagger.Services;

/// <summary>Stores dynamic tagging rules in a private Azure Blob.</summary>
public sealed class BlobTaggingConfigurationStore : ITaggingConfigurationStore
{
    /// <summary>The stable Blob name containing the effective dynamic rules.</summary>
    public const string BlobName = "tagging-configuration.json";

    /// <summary>Web-default (camelCase) JSON options used for the stored rule document.</summary>
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    /// <summary>The blob at <see cref="BlobName"/> containing the dynamic rules.</summary>
    private readonly BlobClient _blobClient;

    /// <summary>Supplies the app-setting rules used until the first rule change is stored.</summary>
    private readonly OptionsTaggingRuleProvider _fallbackProvider;

    /// <summary>Initializes the Blob-backed configuration store.</summary>
    /// <param name="blobClient">The blob that stores the dynamic rules.</param>
    /// <param name="fallbackProvider">The app-setting rule provider used when the blob does not exist.</param>
    public BlobTaggingConfigurationStore(
        BlobClient blobClient,
        OptionsTaggingRuleProvider fallbackProvider)
    {
        _blobClient = blobClient;
        _fallbackProvider = fallbackProvider;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TaggingRuleOptions>> GetRulesAsync(
        CancellationToken cancellationToken) =>
        (await GetSnapshotAsync(cancellationToken)).Rules;

    /// <inheritdoc />
    /// <remarks>
    /// Stored rules are re-validated on every read so a manually edited blob cannot inject invalid
    /// rules. A missing blob yields the app-setting rules with source <c>appSetting</c> and no ETag.
    /// </remarks>
    public async Task<TaggingConfigurationSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _blobClient.DownloadContentAsync(cancellationToken);
            var rules = response.Value.Content.ToObjectFromJson<List<TaggingRuleOptions>>(
                SerializerOptions) ?? [];
            return new TaggingConfigurationSnapshot(
                TaggingConfigurationService.NormalizeAndValidate(rules),
                response.Value.Details.ETag.ToString(),
                response.Value.Details.LastModified,
                "blob");
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return new TaggingConfigurationSnapshot(
                await _fallbackProvider.GetRulesAsync(cancellationToken),
                null,
                null,
                "appSetting");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Without <paramref name="expectedETag"/>, the write only succeeds when the blob does not yet
    /// exist; with it, the write only succeeds when the blob is unchanged. Storage 409 and 412
    /// responses are surfaced as <see cref="TaggingConfigurationConflictException"/>.
    /// </remarks>
    public async Task<TaggingConfigurationSnapshot> ReplaceAsync(
        IReadOnlyList<TaggingRuleOptions> rules,
        string? expectedETag,
        CancellationToken cancellationToken)
    {
        var normalizedRules = TaggingConfigurationService.NormalizeAndValidate(rules);
        var conditions = expectedETag is null
            ? new BlobRequestConditions { IfNoneMatch = ETag.All }
            : new BlobRequestConditions { IfMatch = new ETag(expectedETag) };

        try
        {
            var response = await _blobClient.UploadAsync(
                BinaryData.FromObjectAsJson(normalizedRules, SerializerOptions),
                new BlobUploadOptions
                {
                    Conditions = conditions,
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = "application/json; charset=utf-8"
                    }
                },
                cancellationToken);
            return new TaggingConfigurationSnapshot(
                normalizedRules,
                response.Value.ETag.ToString(),
                response.Value.LastModified,
                "blob");
        }
        catch (RequestFailedException exception) when (exception.Status == 409 || exception.Status == 412)
        {
            throw new TaggingConfigurationConflictException(exception);
        }
    }
}