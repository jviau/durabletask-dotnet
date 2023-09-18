// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using Azure;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// A reader for <see cref="ActivityWorkItem"/>.
/// </summary>
class ActivityWorkItemSource : IWorkItemSource
{
    const int MaxMessages = 100;
    readonly Channel<WorkItem> channel = Channel.CreateBounded<WorkItem>(
        new BoundedChannelOptions(MaxMessages) { SingleReader = true, SingleWriter = true, });

    readonly QueueServiceClient queues;
    readonly QueueClient queue;
    readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityWorkItemSource"/> class.
    /// </summary>
    /// <param name="prefix">The queue prefix name.</param>
    /// <param name="queues">The queue provider.</param>
    /// <param name="logger">The logger.</param>
    public ActivityWorkItemSource(string prefix, QueueServiceClient queues, ILogger<ActivityWorkItemSource> logger)
    {
        this.queues = Check.NotNull(queues);
        this.queue = queues.GetQueueClient(prefix + "activities");
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
            }
        }
    }

    async Task ReceiveLoopAsync(CancellationToken cancellation)
    {
        await this.queue.CreateIfNotExistsAsync(cancellationToken: cancellation);
        while (await this.channel.Writer.WaitToWriteAsync(cancellation))
        {
            int maxMessages = MaxMessages - this.channel.Reader.Count;
            if (maxMessages > 32)
            {
                maxMessages = 32;
            }

            QueueMessage[] messages = await this.queue.ReceiveMessagesAsync(
                maxMessages, cancellationToken: cancellation);

            foreach (QueueMessage message in messages)
            {
                WorkDispatch? work = await message.Body.ToObjectAsync<WorkDispatch>(
                    StorageSerializer.Default, cancellation);

                if (work is not null)
                {
                    work.Populate(message);
                    AzureStorageActivityWorkItem workItem = new(
                        work, this.queue, this.queues.GetQueueClient(work.Parent!.QueueName!), this.logger);
                    this.channel.Writer.TryWrite(workItem);
                }
            }
        }
    }
}
