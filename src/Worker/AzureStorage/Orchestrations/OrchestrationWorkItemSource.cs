// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using Azure;
using Azure.Data.Tables;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// A queue for reading and processing orchestration messages.
/// </summary>
class OrchestrationWorkItemSource : IWorkItemSource
{
    const int BufferSize = 100;
    static readonly string[] StateSelect = new[] { "Name", "ParentName", "ParentId" };

    readonly Channel<WorkItem> workItems = Channel.CreateBounded<WorkItem>(
        new BoundedChannelOptions(BufferSize) { SingleReader = true, SingleWriter = true });

    readonly OrchestrationMessageRouter router;
    readonly QueueClient orchestrations;
    readonly QueueClient activities;
    readonly TableClient history;
    readonly TableClient state;
    readonly ILoggerFactory loggerFactory;
    readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestrationWorkItemSource"/> class.
    /// </summary>
    /// <param name="orchestrations">The orchestration queue.</param>
    /// <param name="activities">The activity queue.</param>
    /// <param name="history">The orchestration history table.</param>
    /// <param name="state">The orchestration state table.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public OrchestrationWorkItemSource(
        QueueClient orchestrations,
        QueueClient activities,
        TableClient history,
        TableClient state,
        ILoggerFactory loggerFactory)
    {
        this.orchestrations = Check.NotNull(orchestrations);
        this.activities = Check.NotNull(activities);
        this.history = Check.NotNull(history);
        this.state = Check.NotNull(state);

        this.loggerFactory = Check.NotNull(loggerFactory);
        this.logger = loggerFactory.CreateLogger<OrchestrationWorkItemSource>();
        this.router = new(this.logger);
    }

    /// <summary>
    /// Gets the work item channel reader.
    /// </summary>
    public ChannelReader<WorkItem> Reader => this.workItems.Reader;

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellation)
    {
        await this.orchestrations.CreateIfNotExistsAsync(cancellationToken: cancellation);
        await this.state.CreateIfNotExistsAsync(cancellation);
        await this.history.CreateIfNotExistsAsync(cancellation);
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

        this.workItems.Writer.TryComplete();
    }

    async Task ReceiveLoopAsync(CancellationToken cancellation)
    {
        while (await this.workItems.Writer.WaitToWriteAsync(cancellation))
        {
            int maxMessages = BufferSize - this.workItems.Reader.Count;
            if (maxMessages > 32)
            {
                maxMessages = 32;
            }

            QueueMessage[] messages = await this.orchestrations.ReceiveMessagesAsync(
                maxMessages, cancellationToken: cancellation);

            foreach (QueueMessage message in messages)
            {
                WorkMessage? work = await message.Body.ToObjectAsync<WorkMessage>(
                    StorageSerializer.Default, cancellation);
                if (work is null)
                {
                    continue;
                }

                work.Populate(message);
                if (!await this.router.DeliverAsync(work.Id, work, cancellation))
                {
                    AzureStorageOrchestrationWorkItem? workItem = await this.CreateWorkItemAsync(work, cancellation);
                    if (workItem is not null)
                    {
                        this.logger.LogInformation("Staging new orchestration work item: {InstanceId}", workItem.Id);
                        await this.workItems.Writer.WriteAsync(workItem, cancellation);
                    }
                }
            }
        }
    }

    async Task<AzureStorageOrchestrationWorkItem?> CreateWorkItemAsync(
        WorkMessage work, CancellationToken cancellation)
    {
        OrchestrationEnvelope? envelope = await this.GetEnvelopeAsync(work, cancellation);
        if (envelope is null)
        {
            // We have no state, orchestration was probably purged. Delete it.
            this.logger.LogWarning("Received message for non-existant orchestration.");
            await this.orchestrations.DeleteMessageAsync(work.MessageId, work.PopReceipt, cancellation);
            return null;
        }

        WorkDispatchReader reader = await this.router.InitializeAsync(work, cancellation);
        TableOrchestrationStore store = new(
            work.Id, this.history, this.state, this.loggerFactory.CreateLogger<TableOrchestrationStore>());
        AzureOrchestrationQueue queue = new(envelope.Value, reader, this.orchestrations, this.activities);
        StorageOrchestrationSession session = new(
            envelope.Value, store, queue, this.loggerFactory.CreateLogger<StorageOrchestrationSession>());
        return new AzureStorageOrchestrationWorkItem(
            envelope.Value, session, this.loggerFactory.CreateLogger<AzureStorageOrchestrationWorkItem>())
        {
            FirstRun = work.Message is ExecutionStarted,
        };
    }

    async Task<OrchestrationEnvelope?> GetEnvelopeAsync(WorkMessage work, CancellationToken cancellation)
    {
        if (work.Message is SubOrchestrationScheduled scheduled)
        {
            OrchestrationInstanceEntity instance = new()
            {
                PartitionKey = work.Id,
                RowKey = "state",
                CreatedAt = scheduled.Timestamp,
                Input = scheduled.Input,
                Name = scheduled.Name,
                Status = RuntimeStatus.Running,
                ParentId = work.Parent?.Id,
                ParentName = work.Parent?.Name,
                ScheduledId = scheduled.Id,
            };

            await this.state.AddEntityAsync(instance, cancellation);
            ParentOrchestrationInstance? p = work.Parent is null
                ? null : new ParentOrchestrationInstance(work.Parent.Name, work.Parent.Id);

            work.Message = new ExecutionStarted(scheduled.Timestamp, scheduled.Input);
            return new OrchestrationEnvelope(work.Id, scheduled.Name, p) { ScheduledId = scheduled.Id };
        }

        NullableResponse<OrchestrationInstanceEntity> response = await this.state
            .GetEntityIfExistsAsync<OrchestrationInstanceEntity>(work.Id, "state", StateSelect, cancellation);
        if (!response.HasValue)
        {
            return null;
        }

        OrchestrationInstanceEntity entity = response.Value;
        ParentOrchestrationInstance? parent = null;
        if (entity.ParentId is string parentId)
        {
            parent = new(entity.ParentName!, parentId);
        }

        return new OrchestrationEnvelope(work.Id, entity.Name!, parent) { ScheduledId = entity.ScheduledId };
    }
}
