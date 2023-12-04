// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using Azure.Data.Tables;
using Azure.Storage.Queues;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// Represents a queue for a single orchestration.
/// </summary>
interface IOrchestrationQueue
{
    /// <summary>
    /// Gets a channel reader for new messages.
    /// </summary>
    ChannelReader<WorkMessage> Reader { get; }

    /// <summary>
    /// Deletes a message.
    /// </summary>
    /// <param name="message">The message to delete.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task that completes when the message is deleted.</returns>
    Task DeleteMessageAsync(WorkMessage message, CancellationToken cancellation = default);

    /// <summary>
    /// Sends a message to the outbound queue.
    /// </summary>
    /// <param name="message">The message to add.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task that completes when the message has been added.</returns>
    Task SendMessageAsync(OrchestrationMessage message, CancellationToken cancellation = default);

    /// <summary>
    /// Release this queue, signalling no further messages will be read.
    /// </summary>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task that completes when this has been released.</returns>
    ValueTask ReleaseAsync(CancellationToken cancellation = default);
}

/// <summary>
/// An orchestration queue using Azure storage queues.
/// </summary>
class AzureOrchestrationQueue : IOrchestrationQueue
{
    readonly OrchestrationEnvelope envelope;
    readonly WorkDispatchReader reader;
    readonly QueueClient orchestrationQueue;
    readonly QueueClient activityQueue;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureOrchestrationQueue"/> class.
    /// </summary>
    /// <param name="envelope">The orchestration envelope.</param>
    /// <param name="reader">The work dispatch reader.</param>
    /// <param name="orchestrationQueue">The queue this orchestration belongs to.</param>
    /// <param name="activityQueue">The queue for activities.</param>
    public AzureOrchestrationQueue(
        OrchestrationEnvelope envelope,
        WorkDispatchReader reader,
        QueueClient orchestrationQueue,
        QueueClient activityQueue)
    {
        this.envelope = Check.NotDefault(envelope);
        this.reader = Check.NotNull(reader);
        this.orchestrationQueue = Check.NotNull(orchestrationQueue);
        this.activityQueue = Check.NotNull(activityQueue);
    }

    /// <inheritdoc/>
    public ChannelReader<WorkMessage> Reader => this.reader;

    /// <inheritdoc/>
    public Task DeleteMessageAsync(WorkMessage message, CancellationToken cancellation = default)
    {
        Check.NotNull(message);
        return this.orchestrationQueue.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellation);
    }

    /// <inheritdoc/>
    public ValueTask ReleaseAsync(CancellationToken cancellation = default) => this.reader.DisposeAsync();

    /// <inheritdoc/>
    public async Task SendMessageAsync(OrchestrationMessage message, CancellationToken cancellation = default)
    {
        Check.NotNull(message);

        QueueClient targetQueue = this.orchestrationQueue;
        if (message is TaskActivityScheduled)
        {
            targetQueue = this.activityQueue;
        }

        BinaryData data = this.GetData(message, out TimeSpan? delay);
        await targetQueue.SendMessageAsync(data, delay, cancellationToken: cancellation);
    }

    BinaryData GetData(OrchestrationMessage message, out TimeSpan? delay)
    {
        WorkParent? parent = message is WorkScheduledMessage
            ? new(this.envelope.Id, this.envelope.Name, this.orchestrationQueue.Name)
            : null;

        delay = null;
        if (message is TimerScheduled timer)
        {
            delay = timer.FireAt.Subtract(DateTimeOffset.UtcNow);
            if (delay < TimeSpan.Zero)
            {
                delay = null;
            }
        }

        WorkMessage m = new(this.GetDispatchId(message), message) { Parent = parent };
        return StorageSerializer.Default.Serialize(m);
    }

    string GetDispatchId(OrchestrationMessage message)
    {
        return message switch
        {
            TimerScheduled => this.envelope.Id,
            TaskActivityScheduled m => $"{this.envelope.Id}::{m.Name}@{m.Id}",
            SubOrchestrationScheduled m => m.Options?.InstanceId ?? Guid.NewGuid().ToString("N"),
            SubOrchestrationCompleted => this.envelope.Parent!.InstanceId,
            EventSent m => m.InstanceId,
            _ => throw new InvalidOperationException($"Message type {message.GetType()} is not a dispatchable message."),
        };
    }
}
