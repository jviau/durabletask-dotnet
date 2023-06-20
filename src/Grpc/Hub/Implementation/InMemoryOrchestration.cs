// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using DurableTask.Core;
using DurableTask.Core.History;
using DurableTask.Core.Query;
using Grpc.Core;

namespace Microsoft.DurableTask.Grpc.Hub.Implementation;

/// <summary>
/// Represents the state, history, and execution lifetime of an orchestration in memory.
/// </summary>
class InMemoryOrchestration
{
    readonly object sync = new();
    readonly TaskCompletionSource tcs = new();
    readonly ChannelWriter<InMemoryOrchestration> queue;

    List<TaskMessage> pendingMessages = new();
    int processingState; // 0 = not queued, not running. 1 = queued. 2 = running.

    DateTimeOffset lastUpdateTime;
    OrchestrationRuntimeState? runtimeState;
    OrchestrationState? state;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryOrchestration"/> class.
    /// </summary>
    /// <param name="id">The orchestration instance ID.</param>
    /// <param name="queue">The ready-to-run queue.</param>
    public InMemoryOrchestration(string id, ChannelWriter<InMemoryOrchestration> queue)
    {
        this.Id = Check.NotNullOrEmpty(id);
        this.queue = Check.NotNull(queue);
    }

    /// <summary>
    /// Gets the orchestration ID.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the completion tracking task for this orchestration.
    /// </summary>
    public Task Completion => this.tcs.Task;

    /// <summary>
    /// Gets the state of the orchestration.
    /// </summary>
    public OrchestrationState State
    {
        get
        {
            if (this.state is { } s)
            {
                return s;
            }

            lock (this.sync)
            {
                if (this.runtimeState?.IsValid != true)
                {
                    throw new InvalidOperationException("Invalid runtime state, cannot produce state.");
                }

                // A PastEvents count of 0 and a processing state of 0 or 1 means that this orchestration has yet to be
                // ran at least once. So we will force a status of "Pending" here.
                OrchestrationStatus status = this.runtimeState.PastEvents.Count == 0 && this.processingState < 2
                    ? OrchestrationStatus.Pending : this.runtimeState.OrchestrationStatus;

                this.state = new()
                {
                    CreatedTime = this.runtimeState.CreatedTime,
                    CompletedTime = this.runtimeState.CompletedTime,
                    LastUpdatedTime = this.lastUpdateTime.UtcDateTime,
                    ScheduledStartTime = this.runtimeState.ExecutionStartedEvent!.ScheduledStartTime,
                    Name = this.runtimeState.Name,
                    Version = this.runtimeState.Version,
                    Generation = this.runtimeState.ExecutionStartedEvent!.Generation,
                    OrchestrationInstance = this.runtimeState.OrchestrationInstance!,
                    ParentInstance = this.runtimeState.ParentInstance,
                    OrchestrationStatus = status,
                    Status = this.runtimeState.Status,
                    Size = this.runtimeState.Size,
                    CompressedSize = this.runtimeState.CompressedSize,
                    Input = this.runtimeState.Input,
                    Output = this.runtimeState.Output,
                    FailureDetails = this.runtimeState.FailureDetails,
                    Tags = this.runtimeState.Tags,
                };

                return this.state;
            }
        }
    }

    /// <summary>
    /// Adds the message to this orchestration, prepapring it for delivery on next execution.
    /// </summary>
    /// <param name="message">The message to add.</param>
    /// <param name="cancellation">Cancels adding this message.</param>
    /// <returns>A task that completes when the message has been added.</returns>
    public ValueTask AddMessageAsync(TaskMessage message, CancellationToken cancellation = default)
    {
        if (this.AddMessage(message))
        {
            return this.queue.WriteAsync(this, cancellation);
        }

        return default;
    }

    /// <summary>
    /// Locks this orchestration for processing.
    /// </summary>
    /// <returns>The work item for processing.</returns>
    public TaskOrchestrationWorkItem LockForProcessing()
    {
        lock (this.sync)
        {
            if (this.processingState != 1)
            {
                throw new InvalidOperationException("Orchestration is not yet ready for processing.");
            }

            if (this.runtimeState is null)
            {
                throw new InvalidOperationException("Orchestration has not yet recieved an execution started message.");
            }

            this.processingState = 2;
            return new WorkItem(this);
        }
    }

    /// <summary>
    /// Checks if this orchestration matches the provided query.
    /// </summary>
    /// <param name="query">The query to match against.</param>
    /// <returns><c>true</c> if a match, <c>false</c> otherwise.</returns>
    public bool Matches(Query query)
    {
        if (this.runtimeState is not { } state)
        {
            return false;
        }

        if (query.CreatedFrom is { } from && from > state.CreatedTime)
        {
            return false;
        }

        if (query.CreatedTo is { } to && to < state.CreatedTime)
        {
            return false;
        }

        if (query.RuntimeStatuses is { } statuses && !statuses.Contains(state.OrchestrationStatus))
        {
            return false;
        }

        if (query.InstanceIdPrefix is { } prefix && !this.Id.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    bool AddMessage(TaskMessage message)
    {
        Check.NotNull(message);
        Check.Argument(
            this.Id.Equals(message.OrchestrationInstance.InstanceId, StringComparison.Ordinal),
            nameof(message),
            "Message is for an incorrect orchestration instance.");

        if (this.runtimeState is null)
        {
            Check.Argument(
                message.Event is ExecutionStartedEvent,
                nameof(message),
                "First message added must be an execution started event.");
        }

        lock (this.sync)
        {
            this.pendingMessages.Add(message);
            if (message.Event is ExecutionStartedEvent started)
            {
                this.SetPendingExecutionState(started);
                this.runtimeState = new(); // reset.
            }

            if (this.processingState == 0)
            {
                // 0 = not running, not processing
                // Caller MUST queue this.
                this.processingState = 1; // 1 = queued.
                return true;
            }
        }

        return false;
    }

    void SetPendingExecutionState(ExecutionStartedEvent started)
    {
        lock (this.sync)
        {
            this.state = new()
            {
                CreatedTime = this.runtimeState?.CreatedTime ?? DateTime.UtcNow,
                CompletedTime = default,
                LastUpdatedTime = DateTime.UtcNow,
                ScheduledStartTime = this.runtimeState?.ExecutionStartedEvent?.ScheduledStartTime
                    ?? started.ScheduledStartTime,
                Name = started.Name,
                Version = started.Version,
                Generation = started.Generation,
                OrchestrationInstance = started.OrchestrationInstance,
                ParentInstance = started.ParentInstance,
                OrchestrationStatus = OrchestrationStatus.Pending,
                Status = null,
                Size = 0,
                CompressedSize = 0,
                Input = started.Input,
                Output = null,
                FailureDetails = null,
                Tags = started.Tags,
            };
        }
    }

    void CommitRuntimeState(OrchestrationRuntimeState newState)
    {
        Check.NotNull(newState);

        lock (this.sync)
        {
            if (this.processingState != 2)
            {
                throw new InvalidOperationException("Invalid processing state to commit new state from.");
            }

            this.state = null;
            this.lastUpdateTime = DateTimeOffset.UtcNow;
            this.runtimeState = new(newState.Events) { Status = newState.Status };
            if (this.runtimeState.OrchestrationStatus.IsTerminal())
            {
                this.tcs.TrySetResult();
            }
        }
    }

    ValueTask ReleaseProcessingLockAsync(CancellationToken cancellation = default)
    {
        bool shouldQueue = false;
        lock (this.sync)
        {
            if (this.processingState != 2)
            {
                throw new InvalidOperationException("Invalid processing state to release lock from.");
            }

            // Check if any new events came in while processing. If so, we need to queue again.
            shouldQueue = this.pendingMessages.Count > 0;
            this.processingState = shouldQueue ? 1 : 0;
        }

        if (shouldQueue)
        {
            return this.queue.WriteAsync(this, cancellation);
        }

        return default;
    }

    /// <summary>
    /// A query record used to see if this orchestration matches some defined constraints.
    /// </summary>
    /// <param name="CreatedFrom">The creation time lower bound.</param>
    /// <param name="CreatedTo">The creation time upper bound.</param>
    /// <param name="RuntimeStatuses">Allow runtime statuses.</param>
    /// <param name="InstanceIdPrefix">Instance ID prefix.</param>
    public record struct Query(
        DateTimeOffset? CreatedFrom,
        DateTimeOffset? CreatedTo,
        IEnumerable<OrchestrationStatus>? RuntimeStatuses,
        string? InstanceIdPrefix)
    {
        public static implicit operator Query(PurgeInstanceFilter filter) => FromPurgeFilter(filter);

        public static implicit operator Query(OrchestrationQuery query) => FromOrchestrationQuery(query);

        /// <summary>
        /// Creates a query struct from a <see cref="PurgeInstanceFilter"/>.
        /// </summary>
        /// <param name="filter">The filter to create the query from.</param>
        /// <returns>The converted query.</returns>
        public static Query FromPurgeFilter(PurgeInstanceFilter filter)
        {
            return new(filter.CreatedTimeFrom, filter.CreatedTimeTo, filter.RuntimeStatus, null);
        }

        /// <summary>
        /// Creates a query struct from a <see cref="OrchestrationQuery"/>.
        /// </summary>
        /// <param name="query">The orchestration query to create the query from.</param>
        /// <returns>The converted query.</returns>
        public static Query FromOrchestrationQuery(OrchestrationQuery query)
        {
            return new(query.CreatedTimeFrom, query.CreatedTimeTo, query.RuntimeStatus, query.InstanceIdPrefix);
        }
    }

    /// <summary>
    /// The work item for this orchestration.
    /// </summary>
    public class WorkItem : TaskOrchestrationWorkItem
    {
        readonly InMemoryOrchestration orchestration;

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkItem"/> class.
        /// </summary>
        /// <param name="orchestration">The orchestration this work item is for.</param>
        public WorkItem(InMemoryOrchestration orchestration)
        {
            lock (orchestration.sync)
            {
                this.orchestration = Check.NotNull(orchestration);
                this.InstanceId = this.orchestration.Id;
                this.LockedUntilUtc = DateTime.MaxValue;
                this.NewMessages = this.orchestration.pendingMessages;
                this.orchestration.pendingMessages = new();
                this.RenewRuntimeState();
            }
        }

        /// <summary>
        /// Abandons any changes to this work item.
        /// </summary>
        public void Abandon()
        {
            lock (this.orchestration.sync)
            {
                this.orchestration.pendingMessages.InsertRange(0, this.NewMessages);
                this.NewMessages = new List<TaskMessage>();
            }
        }

        /// <summary>
        /// Commites the current work item, persisting any changes made to it.
        /// </summary>
        public void Commit()
        {
            this.orchestration.CommitRuntimeState(this.OrchestrationRuntimeState);
        }

        /// <summary>
        /// Release this work item.
        /// </summary>
        /// <param name="cancellation">The cancellation token.</param>
        /// <returns>A task that completes when the work item is released.</returns>
        public ValueTask ReleaseAsync(CancellationToken cancellation = default)
        {
            return this.orchestration.ReleaseProcessingLockAsync(cancellation);
        }

        void RenewRuntimeState()
        {
            if (this.orchestration.runtimeState is not { } current)
            {
                throw new InvalidOperationException("Orchestration has not yet recieved an execution started message.");
            }

            OrchestrationRuntimeState state = new(current.PastEvents);
            foreach (HistoryEvent e in current.NewEvents)
            {
                state.AddEvent(e);
            }

            this.OrchestrationRuntimeState = state;
        }
    }
}
