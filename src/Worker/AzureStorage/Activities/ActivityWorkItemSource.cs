// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using Azure;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// A reader for <see cref="ActivityWorkItem"/>.
/// </summary>
class ActivityWorkItemSource : IWorkItemSource
{
    const int MaxMessages = 100;
    readonly Channel<WorkItem> channel = Channel.CreateBounded<WorkItem>(
        new BoundedChannelOptions(MaxMessages) { SingleReader = true, SingleWriter = true, });

    readonly ActivityWorkItemFactory workItemFactory;
    readonly DurableStorageClientOptions options;
    readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityWorkItemSource"/> class.
    /// </summary>
    /// <param name="factory">The queue prefix name.</param>
    /// <param name="options">The storage client options.</param>
    /// <param name="logger">The logger.</param>
    public ActivityWorkItemSource(
        ActivityWorkItemFactory factory,
        DurableStorageClientOptions options,
        ILogger<ActivityWorkItemSource> logger)
    {
        this.workItemFactory = Check.NotNull(factory);
        this.options = Check.NotNull(options);
        this.logger = Check.NotNull(logger);
    }

    /// <inheritdoc/>
    public ChannelReader<WorkItem> Reader => this.channel.Reader;

    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken cancellation = default)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await this.ReceiveLoopAsync(cancellation);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                this.logger.LogInformation("Receive loop cancelled.");
                break;
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Receive loop encountered an error.");
                await Task.Delay(TimeSpan.FromSeconds(3), cancellation);
            }
        }
    }

    async Task ReceiveLoopAsync(CancellationToken cancellation)
    {
        QueueClient queue = this.options.ActivityQueue;
        await queue.CreateIfNotExistsAsync(cancellationToken: cancellation);
        while (await this.channel.Writer.WaitToWriteAsync(cancellation))
        {
            int max = QueueHelpers.GetBatchSize(MaxMessages, this.channel.Reader.Count);
            QueueMessage[] messages = await queue.ReceiveMessagesAsync(
                max, cancellationToken: cancellation);

            foreach (QueueMessage message in messages)
            {
                WorkMessage work = WorkMessage.Create(message);
                if (work is not null)
                {
                    ActivityWorkItem workItem = this.workItemFactory.Create(work);
                    this.channel.Writer.TryWrite(workItem);
                }
            }
        }
    }
}
