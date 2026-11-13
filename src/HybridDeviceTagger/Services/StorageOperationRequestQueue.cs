/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * DISCLAIMER: This sample script is not supported under any Microsoft standard support program or service. The sample script is provided AS IS without warranty of any kind. Microsoft further disclaims all implied warranties including, without limitation, any implied warranties of merchantability or of fitness for a particular purpose. The entire risk arising out of the use or performance of the sample scripts and documentation remains with you. In no event shall Microsoft, its authors, or anyone else involved in the creation, production, or delivery of the scripts be liable for any damages whatsoever (including, without limitation, damages for loss of business profits, business interruption, loss of business information, or other pecuniary loss) arising out of the use of or inability to use the sample scripts or documentation, even if Microsoft has been advised of the possibility of such damages.
 */

using Azure.Storage.Queues;
using HybridDeviceTagger.Models;

namespace HybridDeviceTagger.Services;

/// <summary>Enqueues run requests in the private host Storage account.</summary>
public sealed class StorageOperationRequestQueue : IOperationRequestQueue
{
    /// <summary>The Storage queue service client configured for Base64 message encoding.</summary>
    private readonly QueueServiceClient _queueServiceClient;

    /// <summary>Initializes the queue client wrapper.</summary>
    /// <param name="queueServiceClient">A client configured for Base64 message encoding.</param>
    public StorageOperationRequestQueue(QueueServiceClient queueServiceClient)
    {
        _queueServiceClient = queueServiceClient;
    }

    /// <inheritdoc />
    /// <remarks>The request is serialized as camelCase JSON, matching the queue-trigger binding.</remarks>
    public async Task EnqueueAsync(
        string operation,
        OperationRequest request,
        CancellationToken cancellationToken)
    {
        var queueClient = _queueServiceClient.GetQueueClient(OperationQueues.For(operation));
        await queueClient.SendMessageAsync(
            BinaryData.FromObjectAsJson(request, new System.Text.Json.JsonSerializerOptions(
                System.Text.Json.JsonSerializerDefaults.Web)),
            cancellationToken: cancellationToken);
    }
}
