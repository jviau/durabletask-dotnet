// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using System.Threading.Channels;
using DurableTask.Core;
using Microsoft.DurableTask.Grpc.Hub.Implementation;

namespace Microsoft.DurableTask.Grpc.Hub;

/// <summary>
/// A local orchestration service.
/// </summary>
public partial class InMemoryOrchestrationService : IOrchestrationService
{
    readonly bool useSessions;
    readonly OrchestrationStore store = new();
    Channel<TaskMessage> activities = Channel.CreateUnbounded<TaskMessage>();

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryOrchestrationService"/> class.
    /// </summary>
    /// <param name="useSessions">True to use sessions, false otherwise.</param>
    public InMemoryOrchestrationService(bool useSessions = false)
    {
        this.useSessions = useSessions;
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
        Check.NotNull(workItem);
        Check.IsType<ActivityWorkItem>(workItem);
        return this.activities.Writer.WriteAsync(workItem.TaskMessage).AsTask();
    }

    /// <inheritdoc/>
    public Task AbandonTaskOrchestrationWorkItemAsync(TaskOrchestrationWorkItem workItem)
    {
        Check.NotNull(workItem);
        InMemoryOrchestration.WorkItem item = Check.IsType<InMemoryOrchestration.WorkItem>(workItem);
        item.Abandon();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CompleteTaskActivityWorkItemAsync(TaskActivityWorkItem workItem, TaskMessage responseMessage)
    {
        Check.NotNull(workItem);
        Check.IsType<ActivityWorkItem>(workItem);
        Check.NotNull(responseMessage);
        this.store.DeliverMessage(responseMessage);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task CompleteTaskOrchestrationWorkItemAsync(
        TaskOrchestrationWorkItem workItem,
        OrchestrationRuntimeState newOrchestrationRuntimeState,
        IList<TaskMessage> outboundMessages,
        IList<TaskMessage> orchestratorMessages,
        IList<TaskMessage> timerMessages,
        TaskMessage continuedAsNewMessage,
        OrchestrationState orchestrationState)
    {
        Check.NotNull(workItem);
        InMemoryOrchestration.WorkItem item = Check.IsType<InMemoryOrchestration.WorkItem>(workItem);

        foreach (TaskMessage message in timerMessages.Union(orchestratorMessages))
        {
            this.store.DeliverMessage(message);
        }

        foreach (TaskMessage message in outboundMessages)
        {
            await this.activities.Writer.WriteAsync(message);
        }

        item.OrchestrationRuntimeState = newOrchestrationRuntimeState;
        item.Commit();
    }

    /// <inheritdoc/>
    public Task CreateAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    public Task CreateAsync(bool recreateInstanceStore)
    {
        return recreateInstanceStore ? this.DeleteAsync() : Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CreateIfNotExistsAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    public Task DeleteAsync()
    {
        this.store.Reset();
        this.activities = Channel.CreateUnbounded<TaskMessage>();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteAsync(bool deleteInstanceStore) => this.DeleteAsync();

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
    public bool IsMaxMessageCountExceeded(int currentMessageCount, OrchestrationRuntimeState runtimeState) => false;

    /// <inheritdoc/>
    public async Task<TaskActivityWorkItem> LockNextTaskActivityWorkItem(
        TimeSpan receiveTimeout, CancellationToken cancellationToken)
    {
        TaskMessage message = await this.activities.Reader.ReadAsync(cancellationToken);
        return new ActivityWorkItem
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
        InMemoryOrchestration orchestration = await this.store.DequeueAsync(cancellationToken);
        return orchestration.LockForProcessing(this.useSessions);
    }

    /// <inheritdoc/>
    public Task ReleaseTaskOrchestrationWorkItemAsync(TaskOrchestrationWorkItem workItem)
    {
        Check.NotNull(workItem);
        InMemoryOrchestration.WorkItem item = Check.IsType<InMemoryOrchestration.WorkItem>(workItem);
        return item.ReleaseAsync().AsTask();
    }

    /// <inheritdoc/>
    public Task<TaskActivityWorkItem> RenewTaskActivityWorkItemLockAsync(TaskActivityWorkItem workItem)
    {
        Check.NotNull(workItem);
        Check.IsType<ActivityWorkItem>(workItem);
        return Task.FromResult(workItem);
    }

    /// <inheritdoc/>
    public Task RenewTaskOrchestrationWorkItemLockAsync(TaskOrchestrationWorkItem workItem)
    {
        Check.NotNull(workItem);
        Check.IsType<InMemoryOrchestration.WorkItem>(workItem);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StartAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    public Task StopAsync() => this.StopAsync(false);

    /// <inheritdoc/>
    public Task StopAsync(bool isForced) => Task.CompletedTask;

    class ActivityWorkItem : TaskActivityWorkItem
    {
        // marker type to ensure no one is calling us with invalid work items.
    }
}
