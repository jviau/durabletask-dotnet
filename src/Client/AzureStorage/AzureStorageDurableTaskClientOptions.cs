// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Data.Tables;
using Azure.Storage.Queues;

namespace Microsoft.DurableTask.Client.AzureStorage;

/// <summary>
/// Options for the Azure storage <see cref="DurableTaskClient"/>.
/// </summary>
public class AzureStorageDurableTaskClientOptions : DurableTaskClientOptions
{
    /// <summary>
    /// Gets or sets the table client for orchestration instances.
    /// </summary>
    public TableClient InstanceClient { get; set; } = null!;

    /// <summary>
    /// Gets or sets the table client for orchestration history.
    /// </summary>
    public TableClient HistoryClient { get; set; } = null!;

    /// <summary>
    /// Gets or sets the queue client for orchestration messages.
    /// </summary>
    public QueueClient MessageClient { get; set; } = null!;

    /// <summary>
    /// Initialize these clients.
    /// </summary>
    /// <param name="cancellation">The cancellation tokens.</param>
    /// <returns>A task that completes when clients are created.</returns>
    public async Task CreateIfNotExistsAsync(CancellationToken cancellation = default)
    {
        await this.InstanceClient.CreateIfNotExistsAsync(cancellation);
        await this.HistoryClient.CreateIfNotExistsAsync(cancellation);
        await this.MessageClient.CreateIfNotExistsAsync(cancellationToken: cancellation);
    }
}
