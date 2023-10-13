// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// Service for interacting with persisted messages of a single orchestration.
/// </summary>
interface IOrchestrationStore
{
    /// <summary>
    /// Updates the state for this orchestration.
    /// </summary>
    /// <param name="state">The orchestration state.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task that completes when the state has been updated.</returns>
    Task UpdateStateAsync(StateUpdate state, CancellationToken cancellation = default);

    /// <summary>
    /// Append a new history message for this orchestration.
    /// </summary>
    /// <param name="message">The new message.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task that completes when this message has been appended.</returns>
    Task AppendMessageAsync(OrchestrationMessage message, CancellationToken cancellation = default);

    /// <summary>
    /// Gets the orchestration history.
    /// </summary>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>An async pageable containing the orchestration history.</returns>
    AsyncPageable<OrchestrationMessage> GetMessagesAsync(CancellationToken cancellation = default);
}

/// <summary>
/// Orchestration stored via an <see cref="TableClient" />.
/// </summary>
class TableOrchestrationStore : IOrchestrationStore
{
    const string KeyFormat = "000000";
    readonly string id;
    readonly TableClient historyClient;
    readonly TableClient stateClient;
    readonly ILogger logger;
    readonly string historyFilter;
    int index = -1; // -1 since this is pre-incremented during the append phase.

    /// <summary>
    /// Initializes a new instance of the <see cref="TableOrchestrationStore"/> class.
    /// </summary>
    /// <param name="id">The orchestration instance ID.</param>
    /// <param name="historyClient">The table client for orchestration history.</param>
    /// <param name="stateClient">The table client for orchestration state.</param>
    /// <param name="logger">The logger.</param>
    public TableOrchestrationStore(
        string id, TableClient historyClient, TableClient stateClient, ILogger<TableOrchestrationStore> logger)
    {
        this.id = Check.NotNullOrEmpty(id);
        this.historyClient = Check.NotNull(historyClient);
        this.stateClient = Check.NotNull(stateClient);
        this.logger = Check.NotNull(logger);

        // We use an explicit filter like this so we don't capture other rows with the same partition key.
        this.historyFilter = $"PartitionKey eq '{this.id}'";
    }

    /// <inheritdoc/>
    public async Task AppendMessageAsync(OrchestrationMessage message, CancellationToken cancellation = default)
    {
        Check.NotNull(message);
        MessageEntity entity = this.CreateEntity(message);
        if (!await this.historyClient.TryAddEntityAsync(entity, cancellation))
        {
            this.logger.LogWarning(
                "Entity already exists:  PartitionKey={PartitionKey}, RowKey={RowKey}, Type={MessageType}",
                entity.PartitionKey,
                entity.RowKey,
                message.GetType().Name);
        }
    }

    /// <inheritdoc/>
    public AsyncPageable<OrchestrationMessage> GetMessagesAsync(CancellationToken cancellation = default)
    {
        Azure.AsyncPageable<MessageEntity> entities = this.historyClient.QueryAsync<MessageEntity>(
            this.historyFilter, cancellationToken: cancellation);
        return new ShimAsyncPageable<MessageEntity, OrchestrationMessage>(entities, m =>
        {
            this.index++; // Hacky way to count how large our history is at the same time it is loaded in.
            return m.Message.ToObject<OrchestrationMessage>(StorageSerializer.Default)!;
        });
    }

    /// <inheritdoc/>
    public Task UpdateStateAsync(StateUpdate state, CancellationToken cancellation = default)
    {
        StateUpdateEntity entity = StateUpdateEntity.Create(this.id, state);
        return this.stateClient.UpdateEntityAsync(entity, ETag.All, TableUpdateMode.Merge, cancellation);
    }

    MessageEntity CreateEntity(OrchestrationMessage message)
    {
        if (message.Timestamp == default)
        {
            message = message with { Timestamp = DateTimeOffset.UtcNow };
        }

        return new()
        {
            PartitionKey = this.id,
            RowKey = this.GetNextRowKey(),
            Message = StorageSerializer.Default.Serialize(message, typeof(OrchestrationMessage)),
        };
    }

    string GetNextRowKey()
    {
        // Table query results are ordered by partition key then row key. We will use the sequence ID / index of this
        // message, padded with leading zeroes, to ensure history is persisted and queried in the expected order.
        // This does mean an orchestration has a technical limit of only 1,000,000 messages in its history.
        // We can rethink this if anyone has some reason for needing more messages.
        int sequenceId = Interlocked.Increment(ref this.index);
        return sequenceId.ToString(KeyFormat, CultureInfo.InvariantCulture);
    }

    class MessageEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = null!;

        public string RowKey { get; set; } = null!;

        public DateTimeOffset? Timestamp { get; set; }

        public ETag ETag { get; set; }

        public BinaryData Message { get; set; } = null!;
    }

    // This is a partial state entity, including only the values we want to update.
    class StateUpdateEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = null!;

        public string RowKey { get; set; } = null!;

        public DateTimeOffset? Timestamp { get; set; }

        public ETag ETag { get; set; }

        public RuntimeStatus Status { get; init; }

        public string? Result { get; init; }

        public TaskFailureDetails? Failure { get; init; }

        public static StateUpdateEntity Create(string partitionKey, StateUpdate update)
        {
            if (update.SubStatus.HasValue)
            {
                return new WithSubStatus
                {
                    PartitionKey = partitionKey,
                    RowKey = "state",
                    Status = update.Status,
                    Result = update.Result,
                    Failure = update.Failure,
                    SubStatus = update.SubStatus.Value,
                };
            }

            return new StateUpdateEntity
            {
                PartitionKey = partitionKey,
                RowKey = "state",
                Result = update.Result,
                Failure = update.Failure,
                Status = update.Status,
            };
        }

        class WithSubStatus : StateUpdateEntity
        {
            public string? SubStatus { get; init; }
        }
    }
}
