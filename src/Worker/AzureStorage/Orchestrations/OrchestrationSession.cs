// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// A session for running a single orchestration.
/// </summary>
interface IOrchestrationSession
{
    /// <summary>
    /// Gets a task representing completion of this session.
    /// </summary>
    Task Completion { get; }

    /// <summary>
    /// Gets the cancellation token for this orchestration session.
    /// </summary>
    CancellationToken CancellationToken { get; }

    /// <summary>
    /// Gets a reader for new messages.
    /// </summary>
    ChannelReader<WorkMessage> NewMessageReader { get; }

    /// <summary>
    /// Gets the stored history for this orchestration.
    /// </summary>
    /// <returns>An async pageable containing the history.</returns>
    AsyncPageable<OrchestrationMessage> GetHistoryAsync();

    /// <summary>
    /// Writes a new message for this orchestration.
    /// </summary>
    /// <param name="message">The message to write.</param>
    /// <returns>A task that completes when this message is written.</returns>
    Task SendNewMessageAsync(OrchestrationMessage message);

    /// <summary>
    /// Consumes a message that was supplied via <see cref="NewMessageReader"/>.
    /// </summary>
    /// <param name="message">The message to consume.</param>
    /// <returns>A task that completes when the message is consumed.</returns>
    Task ConsumeMessageAsync(WorkMessage message);

    /// <summary>
    /// Update state for this session.
    /// </summary>
    /// <param name="status">The custom status.</param>
    /// <returns>A task that completes when this session is complete.</returns>
    Task UpdateStateAsync(string? status);

    /// <summary>
    /// Release this session, signalling no further messages will be read.
    /// </summary>
    /// <returns>A task that completes when this has been released.</returns>
    ValueTask ReleaseAsync();
}

/// <summary>
/// Azure storage implementation for an orchestration session.
/// </summary>
class StorageOrchestrationSession : IOrchestrationSession
{
    readonly TaskCompletionSource<object?> completion = new();
    readonly OrchestrationEnvelope envelope;
    readonly IOrchestrationStore store;
    readonly IOrchestrationQueue queue;
    readonly ILogger logger;

    ExecutionCompleted? completed;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageOrchestrationSession"/> class.
    /// </summary>
    /// <param name="envelope">The orchestration envelope.</param>
    /// <param name="store">The orchestration store.</param>
    /// <param name="queue">The orchestration queue.</param>
    /// <param name="logger">The logger.</param>
    public StorageOrchestrationSession(
        OrchestrationEnvelope envelope, IOrchestrationStore store, IOrchestrationQueue queue, ILogger logger)
    {
        this.envelope = envelope;
        this.store = Check.NotNull(store);
        this.queue = Check.NotNull(queue);
        this.logger = logger;
    }

    /// <inheritdoc/>
    public Task Completion => this.completion.Task;

    /// <inheritdoc/>
    public CancellationToken CancellationToken => default;

    /// <inheritdoc/>
    public ChannelReader<WorkMessage> NewMessageReader => this.queue.Reader;

    RuntimeStatus RuntimeStatus => this.completed switch
    {
        ExecutionTerminated => RuntimeStatus.Terminated,
        ContinueAsNew => throw new NotSupportedException(),
        ExecutionCompleted c => c.Failure is null ? RuntimeStatus.Completed : RuntimeStatus.Failed,
        null => RuntimeStatus.Running,
    };

    /// <inheritdoc/>
    public async Task ConsumeMessageAsync(WorkMessage message)
    {
        Check.NotNull(message);

        // Consuming a message we first persist it in the store, then we delete it from the queue.
        await this.store.AppendMessageAsync(message.Message, this.CancellationToken);

        // If store persistance succeeded, we do not want this to cancel if we can void it.
        await this.queue.DeleteMessageAsync(message, CancellationToken.None);
    }

    /// <inheritdoc/>
    public AsyncPageable<OrchestrationMessage> GetHistoryAsync()
        => this.store.GetMessagesAsync(this.CancellationToken);

    /// <inheritdoc/>
    public async Task SendNewMessageAsync(OrchestrationMessage message)
    {
        if (this.completed is not null)
        {
            return;
        }

        bool outbound = IsOutboundMessage(message);
        if (outbound)
        {
            // Sending a new message we first deliver to the queue, then we will persist the delivery to the store.
            this.logger.LogInformation("Delivering message: {MessageType}", message.GetType().Name);
            await this.queue.SendMessageAsync(message, this.CancellationToken);
        }

        if (message is ExecutionCompleted completed)
        {
            await this.CompleteOrchestrationAsync(completed);
        }

        // If queue write succeeded, we will not cancel here.
        this.logger.LogInformation("Appending message");
        await this.store.AppendMessageAsync(message, outbound ? CancellationToken.None : this.CancellationToken);
    }

    /// <inheritdoc/>
    public Task UpdateStateAsync(string? status)
    {
        this.logger.LogInformation("Updating orchestration status.");
        return this.UpdateStateCoreAsync(status);
    }

    /// <inheritdoc/>
    public ValueTask ReleaseAsync()
    {
        this.logger.LogInformation("Releasing orchestration.");
        return this.queue.ReleaseAsync(this.CancellationToken);
    }

    static bool IsOutboundMessage(OrchestrationMessage message)
    {
        return message is TimerScheduled or SubOrchestrationScheduled or TaskActivityScheduled or EventSent;
    }

    Task UpdateStateCoreAsync(Optional<string?> status = default)
    {
        StateUpdate update = new()
        {
            Status = this.RuntimeStatus,
            Result = this.completed?.Result,
            Failure = this.completed?.Failure,
            SubStatus = status,
        };

        return this.store.UpdateStateAsync(update, this.CancellationToken);
    }

    async Task CompleteOrchestrationAsync(ExecutionCompleted completed)
    {
        this.logger.LogInformation("Orchestration completed.");
        this.completion.TrySetResult(null);
        this.completed = completed;

        if (this.envelope.Parent is not null)
        {
            SubOrchestrationCompleted notification = new(
                -1, DateTimeOffset.UtcNow, this.envelope.ScheduledId!.Value, completed.Result, completed.Failure);
            await this.queue.SendMessageAsync(notification, this.CancellationToken);
        }

        await this.UpdateStateCoreAsync();
    }
}
