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
