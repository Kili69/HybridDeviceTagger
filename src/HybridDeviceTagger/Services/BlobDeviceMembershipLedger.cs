/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@microsoft.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HybridDeviceTagger.Models;

namespace HybridDeviceTagger.Services;

/// <summary>Stores one JSON ownership record per hybrid device in Azure Blob Storage.</summary>
public sealed class BlobDeviceMembershipLedger : IDeviceMembershipLedger
{
    private readonly BlobContainerClient _containerClient;

    /// <summary>Initializes a new instance of the <see cref="BlobDeviceMembershipLedger"/> class.</summary>
    public BlobDeviceMembershipLedger(BlobContainerClient containerClient)
    {
        _containerClient = containerClient;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, ManagedDeviceMemberships>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        var managedDevices = new Dictionary<Guid, ManagedDeviceMemberships>();
        await foreach (var blob in _containerClient.GetBlobsAsync(
                           traits: BlobTraits.None,
                           states: BlobStates.None,
                           prefix: "memberships/",
                           cancellationToken: cancellationToken))
        {
            var fileName = Path.GetFileNameWithoutExtension(blob.Name);
            if (!Guid.TryParseExact(fileName, "D", out var deviceId))
            {
                continue;
            }

            try
            {
                var response = await _containerClient
                    .GetBlobClient(blob.Name)
                    .DownloadContentAsync(cancellationToken);
                var memberships = response.Value.Content.ToObjectFromJson<ManagedDeviceMemberships>();
                if (memberships is not null)
                {
                    managedDevices[deviceId] = memberships;
                }
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                // The entry was removed after listing; the next run will observe the current state.
            }
        }

        return managedDevices;
    }

    /// <inheritdoc />
    public async Task<ManagedDeviceMemberships?> GetAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var blobClient = GetBlobClient(deviceId);
        try
        {
            var response = await blobClient.DownloadContentAsync(cancellationToken);
            return response.Value.Content.ToObjectFromJson<ManagedDeviceMemberships>();
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetAsync(
        Guid deviceId,
        ManagedDeviceMemberships memberships,
        CancellationToken cancellationToken)
    {
        var blobClient = GetBlobClient(deviceId);
        if (memberships.EntraGroupIds.Count == 0 &&
            memberships.AdministrativeUnitIds.Count == 0 &&
            memberships.ExtensionAttributes.Count == 0)
        {
            await blobClient.DeleteIfExistsAsync(
                DeleteSnapshotsOption.IncludeSnapshots,
                cancellationToken: cancellationToken);
            return;
        }

        await blobClient.UploadAsync(
            BinaryData.FromObjectAsJson(memberships),
            overwrite: true,
            cancellationToken);
    }

    private BlobClient GetBlobClient(Guid deviceId) =>
        _containerClient.GetBlobClient($"memberships/{deviceId:D}.json");
}