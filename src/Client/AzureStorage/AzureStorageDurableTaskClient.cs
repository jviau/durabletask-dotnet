// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq.Expressions;
using Azure.Core.Serialization;
using Azure.Data.Tables;
using Azure.Storage.Queues;
using Microsoft.DurableTask.Client.AzureStorage.Implementation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Client.AzureStorage;

/// <summary>
/// Azure storage <see cref="DurableTaskClient"/> implementation.
/// </summary>
class AzureStorageDurableTaskClient : DurableTaskClient
{
    static readonly string[] SelectKeys = new[] { "PartitionKey", "RowKey" };
    static readonly OrchestrationRuntimeStatus[] TerminalStatuses = new[]
    {
        OrchestrationRuntimeStatus.Completed,
        OrchestrationRuntimeStatus.Failed,
        OrchestrationRuntimeStatus.Terminated,
    };

    readonly AzureStorageDurableTaskClientOptions options;
    readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureStorageDurableTaskClient"/> class.
    /// </summary>
    /// <param name="name">The name of this client.</param>
    /// <param name="options">The options monitor.</param>
    /// <param name="logger">The logger.</param>
    public AzureStorageDurableTaskClient(
        string name,
        IOptionsMonitor<AzureStorageDurableTaskClientOptions> options,
        ILogger<AzureStorageDurableTaskClient> logger)
        : base(name)
    {
        Check.NotNull(options);
        this.options = options.Get(name);
        this.logger = Check.NotNull(logger);
    }

    TableClient Instances => this.options.InstanceClient;

    TableClient History => this.options.HistoryClient;

    QueueClient Messages => this.options.MessageClient;

    /// <inheritdoc/>
    public override ValueTask DisposeAsync() => default;

    /// <inheritdoc/>
    public override AsyncPageable<OrchestrationMetadata> GetAllInstancesAsync(OrchestrationQuery? filter = null)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public override Task<OrchestrationMetadata?> GetInstancesAsync(
        string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public override async Task<PurgeResult> PurgeAllInstancesAsync(
        PurgeInstancesFilter filter, CancellationToken cancellation = default)
    {
        Check.NotNull(filter);

        Expression<Func<OrchestrationInstanceEntity, bool>>? expression = null;
        if (filter.CreatedTo is { } to && to < DateTimeOffset.MaxValue)
        {
            expression = (OrchestrationInstanceEntity entity) => entity.CreatedAt <= to;
        }

        if (filter.CreatedFrom is { } from && from > DateTimeOffset.MinValue)
        {
            expression = LambdaExtensions.AndAlso(expression, entity => entity.CreatedAt >= from);
        }

        IEnumerable<OrchestrationRuntimeStatus> statuses = filter.Statuses ?? TerminalStatuses;
        Expression<Func<OrchestrationInstanceEntity, bool>>? or = null;
        foreach (OrchestrationRuntimeStatus status in statuses)
        {
            or = LambdaExtensions.OrElse(or, entity => entity.Status.Equals(status.ToString()));
        }

        expression = LambdaExtensions.AndAlso(expression, or);

        Azure.AsyncPageable<OrchestrationInstanceEntity> query = this.Instances.QueryAsync(
            expression, select: SelectKeys, cancellationToken: cancellation);

        int deleted = 0;
        ParallelOptions options = new()
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount * 25,
            CancellationToken = cancellation,
        };

        await ParallelEx.ForEachAsync(query, options, async (item, cancellation) =>
        {
            Azure.AsyncPageable<MessageEntity> query = this.History.QueryAsync<MessageEntity>(
                x => x.PartitionKey == item.PartitionKey, select: SelectKeys, cancellationToken: cancellation);

            int count = 0;
            List<TableTransactionAction> actions = new();
            await foreach (MessageEntity message in query)
            {
                if (count == 100)
                {
                    await this.History.SubmitTransactionAsync(actions, cancellation);
                    count = 0;
                    actions.Clear();
                }

                actions.Add(new(TableTransactionActionType.Delete, message));
                count++;
            }

            if (actions.Count > 0)
            {
                await this.History.SubmitTransactionAsync(actions, cancellation);
            }

            await this.Instances.DeleteEntityAsync(item.PartitionKey, item.RowKey, cancellationToken: cancellation);
            Interlocked.Increment(ref deleted);
        });

        this.logger.LogInformation("Purged {PurgeCount} instances", deleted);
        return new PurgeResult(deleted);
    }

    /// <inheritdoc/>
    public override Task<PurgeResult> PurgeInstanceAsync(string instanceId, CancellationToken cancellation = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public override Task RaiseEventAsync(
        string instanceId, string eventName, object? eventPayload = null, CancellationToken cancellation = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public override Task ResumeInstanceAsync(
        string instanceId, string? reason = null, CancellationToken cancellation = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public override async Task<string> ScheduleNewOrchestrationInstanceAsync(
        TaskName orchestratorName,
        object? input = null,
        StartOrchestrationOptions? options = null,
        CancellationToken cancellation = default)
    {
        Check.NotDefault(orchestratorName);

        string instanceId = options?.InstanceId ?? Guid.NewGuid().ToString("N");

        string? serializedInput = this.options.DataConverter.Serialize(input);
        OrchestrationInstanceEntity state = new()
        {
            PartitionKey = instanceId,
            RowKey = "state",
            Name = orchestratorName,
            Input = serializedInput,
            Status = OrchestrationRuntimeStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await this.Instances.AddEntityAsync(state, cancellation);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        WorkDispatch dispatch = new(instanceId, new ExecutionStarted(now, serializedInput));
        BinaryData message = await JsonObjectSerializer.Default.SerializeAsync(dispatch, cancellationToken: cancellation);
        TimeSpan? visibility = options?.StartAt is DateTimeOffset startAt
            ? startAt - now : null;

        this.logger.LogInformation("Scheduling new orchestration {InstanceId}", instanceId);
        await this.Messages.SendMessageAsync(message, visibility, cancellationToken: cancellation);
        return instanceId;
    }

    /// <inheritdoc/>
    public override Task SuspendInstanceAsync(
        string instanceId, string? reason = null, CancellationToken cancellation = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public override Task TerminateInstanceAsync(
        string instanceId, object? output = null, CancellationToken cancellation = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public override async Task<OrchestrationMetadata> WaitForInstanceCompletionAsync(
        string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
    {
        Check.NotNullOrEmpty(instanceId);
        while (true)
        {
            OrchestrationInstanceEntity entity = await this.Instances.GetEntityAsync<OrchestrationInstanceEntity>(
                instanceId, "state", cancellationToken: cancellation);

            if (entity.Status.IsTerminal())
            {
                return new OrchestrationMetadata(entity.Name!, instanceId)
                {
                    DataConverter = this.options.DataConverter,
                    RuntimeStatus = entity.Status,
                    CreatedAt = entity.CreatedAt,
                    LastUpdatedAt = entity.Timestamp ?? default,
                    FailureDetails = entity.Failure,
                    SerializedOutput = entity.Result,
                    SerializedInput = entity.Input,
                    SerializedCustomStatus = entity.SubStatus,
                };
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellation);
        }
    }

    /// <inheritdoc/>
    public override Task<OrchestrationMetadata> WaitForInstanceStartAsync(
        string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
    {
        throw new NotImplementedException();
    }
}
