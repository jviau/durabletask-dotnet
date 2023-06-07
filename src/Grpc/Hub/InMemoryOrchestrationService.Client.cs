// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.Exceptions;
using DurableTask.Core.History;
using DurableTask.Core.Query;

namespace Microsoft.DurableTask.Grpc.Hub;

/// <summary>
/// A local orchestration service.
/// </summary>
public partial class InMemoryOrchestrationService
    : IOrchestrationServiceClient, IOrchestrationServiceQueryClient, IOrchestrationServicePurgeClient
{
    static readonly OrchestrationStatus[] Dedupe = new[] { OrchestrationStatus.Pending, OrchestrationStatus.Running };

    /// <inheritdoc/>
    public Task CreateTaskOrchestrationAsync(TaskMessage creationMessage)
    {
        return this.CreateTaskOrchestrationAsync(creationMessage, Dedupe);
    }

    /// <inheritdoc/>
    public Task CreateTaskOrchestrationAsync(TaskMessage creationMessage, OrchestrationStatus[] dedupeStatuses)
    {
        Check.NotNull(creationMessage);
        Check.IsType<ExecutionStartedEvent>(creationMessage.Event, "creationMessage.Event");

        lock (this.store)
        {
            string instanceId = creationMessage.OrchestrationInstance.InstanceId;
            if (this.store.TryGetState(instanceId, out OrchestrationState? state) &&
                dedupeStatuses != null &&
                dedupeStatuses.Contains(state.OrchestrationStatus))
            {
                throw new OrchestrationAlreadyExistsException(
                    $"An orchestration with id '{instanceId}' already exists. It's in the {state.OrchestrationStatus} state.");
            }

            this.store.DeliverMessage(creationMessage);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ForceTerminateTaskOrchestrationAsync(string instanceId, string reason)
    {
        Check.NotNullOrEmpty(instanceId);
        TaskMessage taskMessage = new()
        {
            OrchestrationInstance = new OrchestrationInstance { InstanceId = instanceId },
            Event = new ExecutionTerminatedEvent(-1, reason),
        };

        return this.SendTaskOrchestrationMessageAsync(taskMessage);
    }

    /// <inheritdoc/>
    public Task<string> GetOrchestrationHistoryAsync(string instanceId, string executionId)
    {
        // Not supported in emulator.
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public async Task<IList<OrchestrationState>> GetOrchestrationStateAsync(string instanceId, bool allExecutions)
    {
        OrchestrationState? state = await this.GetOrchestrationStateAsync(instanceId, executionId: null);
        return state is null ? Array.Empty<OrchestrationState>() : new[] { state };
    }

    /// <inheritdoc/>
    public Task<OrchestrationState?> GetOrchestrationStateAsync(string instanceId, string? executionId)
    {
        if (this.store.TryGetState(instanceId, out OrchestrationState? statusRecord))
        {
            if (executionId == null || executionId == statusRecord.OrchestrationInstance.ExecutionId)
            {
                return Task.FromResult<OrchestrationState?>(statusRecord);
            }
        }

        return Task.FromResult<OrchestrationState?>(null!);
    }

    /// <inheritdoc/>
    public Task PurgeOrchestrationHistoryAsync(
        DateTime thresholdDateTimeUtc, OrchestrationStateTimeRangeFilterType timeRangeFilterType)
    {
        // Also not supported in the emulator
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public Task SendTaskOrchestrationMessageAsync(TaskMessage message)
    {
        Check.NotNull(message);
        this.store.DeliverMessage(message);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SendTaskOrchestrationMessageBatchAsync(params TaskMessage[] messages)
    {
        // NOTE: This is not transactionally consistent - some messages may get processed earlier than others.
        foreach (TaskMessage message in messages)
        {
            this.store.DeliverMessage(message);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<OrchestrationState?> WaitForOrchestrationAsync(
        string instanceId, string executionId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Check.NotNullOrEmpty(instanceId);
        if (timeout <= TimeSpan.Zero || timeout == TimeSpan.MaxValue)
        {
            return await this.store.WaitAsync(instanceId, cancellationToken);
        }
        else
        {
            using CancellationTokenSource timeoutCancellationSource = new(timeout);
            using CancellationTokenSource linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellationSource.Token);
            return await this.store.WaitAsync(instanceId, linkedCancellationSource.Token);
        }
    }

    /// <inheritdoc/>
    public Task<OrchestrationQueryResult> GetOrchestrationWithQueryAsync(
        OrchestrationQuery query, CancellationToken cancellationToken)
    {
        Check.NotNull(query);
        IReadOnlyCollection<OrchestrationState> values = this.store.GetAll(query, out string? continuation);
        return Task.FromResult(new OrchestrationQueryResult(values, continuation));
    }

    /// <inheritdoc/>
    public Task<PurgeResult> PurgeInstanceStateAsync(string instanceId)
    {
        Check.NotNull(instanceId);
        PurgeResult result = this.store.TryRemove(instanceId) ? new(1) : new(0);
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<PurgeResult> PurgeInstanceStateAsync(PurgeInstanceFilter purgeInstanceFilter)
    {
        Check.NotNull(purgeInstanceFilter);
        PurgeResult result = new(this.store.RemoveAll(purgeInstanceFilter));
        return Task.FromResult(result);
    }
}
