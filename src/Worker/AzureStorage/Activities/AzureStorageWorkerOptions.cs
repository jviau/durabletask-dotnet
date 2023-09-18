// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// Options for Azure storage.
/// </summary>
public class AzureStorageWorkerOptions
{
    /// <summary>
    /// Gets or sets the name for this hub. This is the name that will be used when creating Azure storage resources.
    /// </summary>
    public string HubName { get; set; } = null!;

    /// <summary>
    /// Gets or sets the table client to use.
    /// </summary>
    public TableServiceClient TableClient { get; set; } = null!;

    /// <summary>
    /// Gets or sets the queue client to use.
    /// </summary>
    public QueueServiceClient QueueClient { get; set; } = null!;

    /// <summary>
    /// Gets or sets the blob client to use.
    /// </summary>
    public BlobServiceClient BlobClient { get; set; } = null!;
}
