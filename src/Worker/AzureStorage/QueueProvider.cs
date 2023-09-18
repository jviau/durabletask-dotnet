// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Storage.Queues;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// Helper for retrieiving <see cref="QueueClient"/>s.
/// </summary>
class QueueProvider
{
    readonly QueueServiceClient service;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueueProvider"/> class.
    /// </summary>
    /// <param name="service">The name of the service.</param>
    public QueueProvider(QueueServiceClient service)
    {
        this.service = Check.NotNull(service);
    }

    /// <summary>
    /// Gets a <see cref="QueueClient"/>.
    /// </summary>
    /// <param name="name">The queue client to get.</param>
    /// <returns>The queue client.</returns>
    public virtual QueueClient Get(string name)
    {
        return this.service.GetQueueClient(name);
    }
}
