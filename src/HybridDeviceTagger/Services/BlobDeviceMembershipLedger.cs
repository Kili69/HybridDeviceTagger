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
        if (memberships.EntraGroupIds.Count == 0 && memberships.AdministrativeUnitIds.Count == 0)
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