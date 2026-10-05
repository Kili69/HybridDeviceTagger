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