// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// A <see cref="ActivityWorkItem"/> that interacts with Azure Storage.
/// </summary>
class AzureStorageActivityWorkItem : ActivityWorkItem
{
    readonly TaskActivityScheduled message;
    readonly WorkDispatch work;
    readonly QueueClient activityQueue;
    readonly QueueClient parentQueue;
    readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureStorageActivityWorkItem"/> class.
    /// </summary>
    /// <param name="work">The work envelope.</param>
    /// <param name="activityQueue">The queue the activity belongs to.</param>
    /// <param name="parentQueue">The queue for the activity parent.</param>
    /// <param name="logger">The logger.</param>
    public AzureStorageActivityWorkItem(
        WorkDispatch work,
        QueueClient activityQueue,
        QueueClient parentQueue,
        ILogger logger)
        : base(VerifyAndGetId(work, out TaskActivityScheduled message), message.Name)
    {
        this.message = message;
        this.work = work;
        this.activityQueue = Check.NotNull(activityQueue);
        this.parentQueue = Check.NotNull(parentQueue);
        this.logger = Check.NotNull(logger);
    }

    /// <inheritdoc/>
    public override string? Input => this.message.Input;

    /// <inheritdoc/>
    public override ValueTask CompleteAsync(string? result)
    {
        TaskActivityCompleted succeeded = new(
            -1, DateTimeOffset.UtcNow, this.message.Id, result, null);
        return new(this.CompleteAsync(succeeded));
    }

    /// <inheritdoc/>
    public override ValueTask FailAsync(Exception exception)
    {
        if (exception is AbortWorkItemException)
        {
            return new(this.AbandonAsync());
        }

        TaskActivityCompleted failed = new(
            -1, DateTimeOffset.UtcNow, this.message.Id, null, TaskFailureDetails.FromException(exception));
        return new(this.CompleteAsync(failed));
    }

    static string VerifyAndGetId(WorkDispatch message, out TaskActivityScheduled scheduled)
    {
        Check.NotNull(message);
        if (message.Message is not TaskActivityScheduled m)
        {
            throw new ArgumentException(
                $"WorkEnvelope must contain a {nameof(TaskActivityScheduled)} message.", nameof(message));
        }

        scheduled = m;
        return message.Id;
    }

    async Task CompleteAsync(TaskActivityCompleted completed)
    {
        WorkDispatch outbound = new(this.work.Parent!.Id, completed);
        BinaryData data = StorageSerializer.Default.Serialize(outbound);
        await this.parentQueue.SendMessageAsync(data);
        await this.activityQueue.DeleteMessageAsync(this.work.MessageId, this.work.PopReceipt);
    }

    Task AbandonAsync()
    {
        TimeSpan timeout = this.GetVisibilityTimeout();
        return this.activityQueue.UpdateMessageAsync(
            this.work.MessageId, this.work.PopReceipt, visibilityTimeout: timeout);
    }

    TimeSpan GetVisibilityTimeout()
    {
        const int maxTimeoutInSeconds = 600;
        int timeoutInSeconds = this.work.DequeueCount <= 30
            ? Math.Min((int)Math.Pow(2, this.work.DequeueCount), maxTimeoutInSeconds)
            : maxTimeoutInSeconds;
        if (timeoutInSeconds == maxTimeoutInSeconds)
        {
            this.logger.LogWarning(
                "Activity message {TaskActivityId} with message ID {MessageId} has been dequeued {DequeueCount} times and reached max visibility timeout.",
                this.Id,
                this.work.MessageId,
                this.work.DequeueCount);
        }

        this.logger.LogInformation(
            "Abandoning activity message {TaskActivityId} with message ID {MessageId} back to queue {StorageQueue} with visibility delay of {DelaySeconds}s.",
            this.Id,
            this.work.MessageId,
            this.activityQueue.Name,
            timeoutInSeconds);
        return TimeSpan.FromSeconds(timeoutInSeconds);
    }
}
