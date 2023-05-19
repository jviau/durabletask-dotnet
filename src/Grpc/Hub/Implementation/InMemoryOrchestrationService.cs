// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using System.Threading.Channels;
using DurableTask.Core;
using DurableTask.Core.Exceptions;
using DurableTask.Core.History;
using DurableTask.Core.Query;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DurableTask.Grpc.Hub;

/// <summary>
/// An in-memory orchestration service.
/// </summary>
partial class InMemoryOrchestrationService :
    IOrchestrationService, IOrchestrationServiceClient,
    IOrchestrationServiceQueryClient, IOrchestrationServicePurgeClient
{
    readonly InMemoryQueue activityQueue = new();
    readonly InMemoryInstanceStore instanceStore;
    readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryOrchestrationService"/> class.
    /// </summary>
    /// <param name="instanceStore">The instance store.</param>
    /// <param name="logger">The logger.</param>
    public InMemoryOrchestrationService(
        InMemoryInstanceStore instanceStore,
        ILogger<InMemoryOrchestrationService>? logger = null)
    {
        this.instanceStore = instanceStore;
        this.logger = logger ?? NullLogger<InMemoryOrchestrationService>.Instance;
    }

    /// <inheritdoc/>
    public int TaskOrchestrationDispatcherCount => 1;

    /// <inheritdoc/>
    public int TaskActivityDispatcherCount => 1;

    /// <inheritdoc/>
    public int MaxConcurrentTaskOrchestrationWorkItems => Environment.ProcessorCount;

    /// <inheritdoc/>
    public int MaxConcurrentTaskActivityWorkItems => Environment.ProcessorCount;

    /// <inheritdoc/>
    public BehaviorOnContinueAsNew EventBehaviourForContinueAsNew => BehaviorOnContinueAsNew.Carryover;

    /// <inheritdoc/>
    public Task AbandonTaskActivityWorkItemAsync(TaskActivityWorkItem workItem)
    {
        this.logger.LogWarning("Abandoning task activity work item {Id}", workItem.Id);
        this.activityQueue.Enqueue(workItem.TaskMessage);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task AbandonTaskOrchestrationWorkItemAsync(TaskOrchestrationWorkItem workItem)
    {
        this.instanceStore.AbandonInstance(workItem.NewMessages);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CompleteTaskActivityWorkItemAsync(TaskActivityWorkItem workItem, TaskMessage responseMessage)
    {
        this.instanceStore.AddMessage(responseMessage);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CompleteTaskOrchestrationWorkItemAsync(
        TaskOrchestrationWorkItem workItem,
        OrchestrationRuntimeState newOrchestrationRuntimeState,
        IList<TaskMessage> outboundMessages,
        IList<TaskMessage> orchestratorMessages,
        IList<TaskMessage> timerMessages,
        TaskMessage continuedAsNewMessage,
        OrchestrationState orchestrationState)
    {
        this.instanceStore.SaveState(
            runtimeState: newOrchestrationRuntimeState,
            statusRecord: orchestrationState,
            newMessages: orchestratorMessages.Union(timerMessages).Append(continuedAsNewMessage).Where(msg => msg != null));

        this.activityQueue.Enqueue(outboundMessages);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CreateAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    public Task CreateAsync(bool recreateInstanceStore)
    {
        if (recreateInstanceStore)
        {
            this.instanceStore.Reset();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CreateIfNotExistsAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    public Task CreateTaskOrchestrationAsync(TaskMessage creationMessage)
    {
        return this.CreateTaskOrchestrationAsync(
            creationMessage,
            new[] { OrchestrationStatus.Pending, OrchestrationStatus.Running });
    }

    /// <inheritdoc/>
    public Task CreateTaskOrchestrationAsync(TaskMessage creationMessage, OrchestrationStatus[]? dedupeStatuses)
    {
        // Lock the instance store to prevent multiple "create" threads from racing with each other.
        lock (this.instanceStore)
        {
            string instanceId = creationMessage.OrchestrationInstance.InstanceId;
            if (this.instanceStore.TryGetState(instanceId, out OrchestrationState? statusRecord) &&
                dedupeStatuses != null &&
                dedupeStatuses.Contains(statusRecord.OrchestrationStatus))
            {
                throw new OrchestrationAlreadyExistsException(
                    $"An orchestration with id '{instanceId}' already exists. It's in the {statusRecord.OrchestrationStatus} state.");
            }

            this.instanceStore.AddMessage(creationMessage);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteAsync() => this.DeleteAsync(true);

    /// <inheritdoc/>
    public Task DeleteAsync(bool deleteInstanceStore)
    {
        if (deleteInstanceStore)
        {
            this.instanceStore.Reset();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ForceTerminateTaskOrchestrationAsync(string instanceId, string reason)
    {
        var taskMessage = new TaskMessage
        {
            OrchestrationInstance = new OrchestrationInstance { InstanceId = instanceId },
            Event = new ExecutionTerminatedEvent(-1, reason),
        };

        return this.SendTaskOrchestrationMessageAsync(taskMessage);
    }

    /// <inheritdoc/>
    public int GetDelayInSecondsAfterOnFetchException(Exception exception)
    {
        return exception is OperationCanceledException ? 0 : 1;
    }

    /// <inheritdoc/>
    public int GetDelayInSecondsAfterOnProcessException(Exception exception)
    {
        return exception is OperationCanceledException ? 0 : 1;
    }

    /// <inheritdoc/>
    public Task<string> GetOrchestrationHistoryAsync(string instanceId, string executionId)
    {
        // Also not supported in the emulator
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public async Task<IList<OrchestrationState>> GetOrchestrationStateAsync(string instanceId, bool allExecutions)
    {
        OrchestrationState state = await this.GetOrchestrationStateAsync(instanceId, executionId: null);
        return new[] { state };
    }

    /// <inheritdoc/>
    public Task<OrchestrationState> GetOrchestrationStateAsync(string instanceId, string? executionId)
    {
        if (this.instanceStore.TryGetState(instanceId, out OrchestrationState? statusRecord))
        {
            if (executionId == null || executionId == statusRecord.OrchestrationInstance.ExecutionId)
            {
                return Task.FromResult(statusRecord);
            }
        }

        return Task.FromResult<OrchestrationState>(null!);
    }

    /// <inheritdoc/>
    public bool IsMaxMessageCountExceeded(int currentMessageCount, OrchestrationRuntimeState runtimeState) => false;

    /// <inheritdoc/>
    public async Task<TaskActivityWorkItem> LockNextTaskActivityWorkItem(
        TimeSpan receiveTimeout, CancellationToken cancellationToken)
    {
        TaskMessage message = await this.activityQueue.DequeueAsync(cancellationToken);
        return new TaskActivityWorkItem
        {
            Id = message.SequenceNumber.ToString(CultureInfo.InvariantCulture),
            LockedUntilUtc = DateTime.MaxValue,
            TaskMessage = message,
        };
    }

    /// <inheritdoc/>
    public async Task<TaskOrchestrationWorkItem> LockNextTaskOrchestrationWorkItemAsync(
        TimeSpan receiveTimeout, CancellationToken cancellationToken)
    {
        (string instanceId, OrchestrationRuntimeState runtimeState, List<TaskMessage> messages) =
            await this.instanceStore.GetNextReadyToRunInstanceAsync(cancellationToken);

        return new TaskOrchestrationWorkItem
        {
            InstanceId = instanceId,
            OrchestrationRuntimeState = runtimeState,
            NewMessages = messages,
            LockedUntilUtc = DateTime.MaxValue,
        };
    }

    /// <inheritdoc/>
    public Task PurgeOrchestrationHistoryAsync(
        DateTime thresholdDateTimeUtc, OrchestrationStateTimeRangeFilterType timeRangeFilterType)
    {
        // Also not supported in the emulator
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public Task ReleaseTaskOrchestrationWorkItemAsync(TaskOrchestrationWorkItem workItem)
    {
        this.instanceStore.ReleaseLock(workItem.InstanceId);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<TaskActivityWorkItem> RenewTaskActivityWorkItemLockAsync(TaskActivityWorkItem workItem)
    {
        return Task.FromResult(workItem); // PeekLock isn't supported
    }

    /// <inheritdoc/>
    public Task RenewTaskOrchestrationWorkItemLockAsync(TaskOrchestrationWorkItem workItem)
    {
        return Task.CompletedTask; // PeekLock isn't supported
    }

    /// <inheritdoc/>
    public Task SendTaskOrchestrationMessageAsync(TaskMessage message)
    {
        this.instanceStore.AddMessage(message);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SendTaskOrchestrationMessageBatchAsync(params TaskMessage[] messages)
    {
        // NOTE: This is not transactionally consistent - some messages may get processed earlier than others.
        foreach (TaskMessage message in messages)
        {
            this.instanceStore.AddMessage(message);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StartAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    public Task StopAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    public Task StopAsync(bool isForced) => Task.CompletedTask;

    /// <inheritdoc/>
    public async Task<OrchestrationState> WaitForOrchestrationAsync(
        string instanceId, string executionId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return await this.instanceStore.WaitForInstanceAsync(instanceId, cancellationToken);
        }
        else
        {
            using CancellationTokenSource timeoutCancellationSource = new(timeout);
            using CancellationTokenSource linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellationSource.Token);
            return await this.instanceStore.WaitForInstanceAsync(instanceId, linkedCancellationSource.Token);
        }
    }

    /// <inheritdoc/>
    public Task<OrchestrationQueryResult> GetOrchestrationWithQueryAsync(
        OrchestrationQuery query, CancellationToken cancellationToken)
    {
        return Task.FromResult(this.instanceStore.GetOrchestrationWithQuery(query));
    }

    /// <inheritdoc/>
    public Task<PurgeResult> PurgeInstanceStateAsync(string instanceId)
    {
        return Task.FromResult(this.instanceStore.PurgeInstanceState(instanceId));
    }

    /// <inheritdoc/>
    public Task<PurgeResult> PurgeInstanceStateAsync(PurgeInstanceFilter purgeInstanceFilter)
    {
        return Task.FromResult(this.instanceStore.PurgeInstanceState(purgeInstanceFilter));
    }

    class InMemoryQueue
    {
        readonly Channel<TaskMessage> innerQueue = Channel.CreateUnbounded<TaskMessage>();

        public void Enqueue(TaskMessage taskMessage)
        {
            if (taskMessage.TryGetScheduledTime(out TimeSpan delay))
            {
                _ = Task.Delay(delay).ContinueWith(t => this.innerQueue.Writer.TryWrite(taskMessage));
            }
            else
            {
                this.innerQueue.Writer.TryWrite(taskMessage);
            }
        }

        public void Enqueue(IEnumerable<TaskMessage> messages)
        {
            foreach (TaskMessage msg in messages)
            {
                this.Enqueue(msg);
            }
        }

        public async Task<TaskMessage> DequeueAsync(CancellationToken cancellationToken)
        {
            return await this.innerQueue.Reader.ReadAsync(cancellationToken);
        }
    }
}
