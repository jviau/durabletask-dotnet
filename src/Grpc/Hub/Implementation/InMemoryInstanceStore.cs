// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using DurableTask.Core;
using DurableTask.Core.History;
using DurableTask.Core.Query;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DurableTask.Grpc.Hub;

/// <summary>
/// Store for the <see cref="InMemoryOrchestrationService"/>.
/// </summary>
class InMemoryInstanceStore
{
    readonly ConcurrentDictionary<string, SerializedInstanceState> store = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, TaskCompletionSource<OrchestrationState>> waiters = new(StringComparer.OrdinalIgnoreCase);
    readonly ReadyToRunQueue readyToRunQueue = new();

    readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryInstanceStore"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public InMemoryInstanceStore(ILogger<InMemoryInstanceStore>? logger)
    {
        this.logger = logger ?? NullLogger<InMemoryInstanceStore>.Instance;
    }

    /// <summary>
    /// Reset this instance store.
    /// </summary>
    public void Reset()
    {
        this.store.Clear();
        this.waiters.Clear();
        this.readyToRunQueue.Reset();
    }

    /// <summary>
    /// Get next item ready to run.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Next item to run.</returns>
    public async Task<(string Id, OrchestrationRuntimeState State, List<TaskMessage> Messages)> GetNextReadyToRunInstanceAsync(
        CancellationToken cancellationToken)
    {
        SerializedInstanceState state = await this.readyToRunQueue.TakeNextAsync(cancellationToken);
        lock (state)
        {
            List<HistoryEvent> history = state.HistoryEventsJson.Select(e => e!.GetValue<HistoryEvent>()).ToList();
            OrchestrationRuntimeState runtimeState = new(history);

            List<TaskMessage> messages = state.MessagesJson.Select(node => node!.GetValue<TaskMessage>()).ToList();
            if (messages == null)
            {
                throw new InvalidOperationException("Should never load state with zero messages.");
            }

            state.IsLoaded = true;

            // There is no "peek-lock" semantic. All dequeued messages are immediately deleted.
            state.MessagesJson.Clear();

            return (state.InstanceId, runtimeState, messages);
        }
    }

    /// <summary>
    /// Try get the state for an instance.
    /// </summary>
    /// <param name="instanceId">The ID to get state for.</param>
    /// <param name="statusRecord">The found state.</param>
    /// <returns>True if found, false otherwise.</returns>
    public bool TryGetState(string instanceId, [NotNullWhen(true)] out OrchestrationState? statusRecord)
    {
        if (!this.store.TryGetValue(instanceId, out SerializedInstanceState? state))
        {
            statusRecord = null;
            return false;
        }

        statusRecord = state.StatusRecordJson?.GetValue<OrchestrationState>();
        return statusRecord != null;
    }

    /// <summary>
    /// Saves state.
    /// </summary>
    /// <param name="runtimeState">State to save.</param>
    /// <param name="statusRecord">Status record to save.</param>
    /// <param name="newMessages">New messages to save.</param>
    public void SaveState(
        OrchestrationRuntimeState runtimeState,
        OrchestrationState statusRecord,
        IEnumerable<TaskMessage> newMessages)
    {
        static bool IsCompleted(OrchestrationRuntimeState runtimeState) =>
            runtimeState.OrchestrationStatus == OrchestrationStatus.Completed ||
            runtimeState.OrchestrationStatus == OrchestrationStatus.Failed ||
            runtimeState.OrchestrationStatus == OrchestrationStatus.Terminated ||
            runtimeState.OrchestrationStatus == OrchestrationStatus.Canceled;

        if (string.IsNullOrEmpty(runtimeState.OrchestrationInstance?.InstanceId))
        {
            throw new ArgumentException("The provided runtime state doesn't contain instance ID information.", nameof(runtimeState));
        }

        string instanceId = runtimeState.OrchestrationInstance.InstanceId;
        string executionId = runtimeState.OrchestrationInstance.ExecutionId;
        SerializedInstanceState state = this.store.GetOrAdd(
            instanceId,
            _ => new SerializedInstanceState(instanceId, executionId));
        lock (state)
        {
            if (state.ExecutionId == null)
            {
                // This orchestration was started by a message without an execution ID.
                state.ExecutionId = executionId;
            }
            else if (state.ExecutionId != executionId)
            {
                // This is a new generation (ContinueAsNew). Erase the old history.
                state.HistoryEventsJson.Clear();
            }

            foreach (TaskMessage msg in newMessages)
            {
                this.AddMessage(msg);
            }

            // Append to the orchestration history
            foreach (HistoryEvent e in runtimeState.NewEvents)
            {
                state.HistoryEventsJson.Add(e);
            }

            state.StatusRecordJson = JsonValue.Create(statusRecord);
            state.IsCompleted = IsCompleted(runtimeState);
        }

        // Notify any waiters of the orchestration completion
        if (IsCompleted(runtimeState) && this.waiters.TryRemove(
            statusRecord.OrchestrationInstance.InstanceId, out TaskCompletionSource<OrchestrationState>? waiter))
        {
            waiter.TrySetResult(statusRecord);
        }
    }

    /// <summary>
    /// Adds a new message.
    /// </summary>
    /// <param name="message">Message to add.</param>
    public void AddMessage(TaskMessage message)
    {
        string instanceId = message.OrchestrationInstance.InstanceId;
        string? executionId = message.OrchestrationInstance.ExecutionId;

        SerializedInstanceState state = this.store.GetOrAdd(
            instanceId, id => new SerializedInstanceState(id, executionId));
        lock (state)
        {
            if (message.Event is ExecutionStartedEvent startEvent)
            {
                OrchestrationState newStatusRecord = new()
                {
                    OrchestrationInstance = startEvent.OrchestrationInstance,
                    CreatedTime = DateTime.UtcNow,
                    LastUpdatedTime = DateTime.UtcNow,
                    OrchestrationStatus = OrchestrationStatus.Pending,
                    Version = startEvent.Version,
                    Name = startEvent.Name,
                    Input = startEvent.Input,
                    ScheduledStartTime = startEvent.ScheduledStartTime,
                };

                state.StatusRecordJson = JsonValue.Create(newStatusRecord);
                state.HistoryEventsJson.Clear();
                state.IsCompleted = false;
            }
            else if (state.IsCompleted)
            {
                // Drop the message since we're completed
                this.logger.LogWarning(
                    "Dropped {EventType} message for instance '{InstanceId}' because the orchestration has already completed.",
                    message.Event.EventType,
                    instanceId);
                return;
            }

            if (message.TryGetScheduledTime(out TimeSpan delay))
            {
                // Not ready for this message yet - delay the enqueue
                _ = Task.Delay(delay).ContinueWith(t => this.AddMessage(message));
                return;
            }

            state.MessagesJson.Add(message);

            if (!state.IsLoaded)
            {
                // The orchestration isn't running, so schedule it to run now.
                // If it is running, it will be scheduled again automatically when it's released.
                this.readyToRunQueue.Schedule(state);
            }
        }
    }

    /// <summary>
    /// Abandon the task messages.
    /// </summary>
    /// <param name="messagesToReturn">Messages to abandon.</param>
    public void AbandonInstance(IEnumerable<TaskMessage> messagesToReturn)
    {
        foreach (TaskMessage message in messagesToReturn)
        {
            this.AddMessage(message);
        }
    }

    /// <summary>
    /// Release the lock for an instance.
    /// </summary>
    /// <param name="instanceId">Instance ID to release lock for.</param>
    public void ReleaseLock(string instanceId)
    {
        if (!this.store.TryGetValue(instanceId, out SerializedInstanceState? state) || !state.IsLoaded)
        {
            throw new InvalidOperationException($"Instance {instanceId} is not in the store or is not loaded!");
        }

        lock (state)
        {
            state.IsLoaded = false;
            if (state.MessagesJson.Count > 0)
            {
                // More messages came in while we were running. Or, messages were abandoned.
                // Put this back into the read-to-run queue!
                this.readyToRunQueue.Schedule(state);
            }
        }
    }

    /// <summary>
    /// Wait for an instance to be in a non-pending state.
    /// </summary>
    /// <param name="instanceId">The instance to wait for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The state of the orchestration when it is in non-pending state.</returns>
    public Task<OrchestrationState> WaitForInstanceAsync(string instanceId, CancellationToken cancellationToken)
    {
        if (this.store.TryGetValue(instanceId, out SerializedInstanceState? state))
        {
            lock (state)
            {
                OrchestrationState? statusRecord = state.StatusRecordJson?.GetValue<OrchestrationState>();
                if (statusRecord != null)
                {
                    if (statusRecord.OrchestrationStatus == OrchestrationStatus.Completed ||
                        statusRecord.OrchestrationStatus == OrchestrationStatus.Failed ||
                        statusRecord.OrchestrationStatus == OrchestrationStatus.Terminated)
                    {
                        // orchestration has already completed
                        return Task.FromResult(statusRecord);
                    }
                }
            }
        }

        // Caller will be notified when the instance completes.
        // The ContinueWith is just to enable cancellation: https://stackoverflow.com/a/25652873/2069
        TaskCompletionSource<OrchestrationState> tcs = this.waiters.GetOrAdd(
            instanceId, _ => new TaskCompletionSource<OrchestrationState>());
        return tcs.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Query orchestrations.
    /// </summary>
    /// <param name="query">The query to run.</param>
    /// <returns>Query results.</returns>
    public OrchestrationQueryResult GetOrchestrationWithQuery(OrchestrationQuery query)
    {
        int startIndex = 0;
        int counter = 0;
        string? continuationToken = query.ContinuationToken;
        if (continuationToken != null)
        {
            if (!int.TryParse(continuationToken, out startIndex))
            {
                throw new InvalidOperationException($"{continuationToken} cannot be parsed to Int32");
            }
        }

        counter = startIndex;

        List<OrchestrationState> results = this.store
            .Skip(startIndex)
            .Where(item =>
            {
                counter++;
                OrchestrationState? statusRecord = item.Value.StatusRecordJson?.GetValue<OrchestrationState>();

                if (statusRecord == null)
                {
                    return false;
                }

                if (query.CreatedTimeFrom != null && query.CreatedTimeFrom > statusRecord.CreatedTime)
                {
                    return false;
                }

                if (query.CreatedTimeTo != null && query.CreatedTimeTo < statusRecord.CreatedTime)
                {
                    return false;
                }

                if (query.RuntimeStatus != null && query.RuntimeStatus.Any()
                && !query.RuntimeStatus.Contains(statusRecord.OrchestrationStatus))
                {
                    return false;
                }

                return query.InstanceIdPrefix == null || statusRecord.OrchestrationInstance.InstanceId
                .StartsWith(query.InstanceIdPrefix, StringComparison.Ordinal);
            })
            .Take(query.PageSize)
            .Select(item => item.Value.StatusRecordJson!.GetValue<OrchestrationState>())
            .ToList();

        string? token = null;
        if (results.Count == query.PageSize)
        {
            token = counter.ToString(CultureInfo.InvariantCulture);
        }

        return new OrchestrationQueryResult(results, token);
    }

    /// <summary>
    /// Purge orchestration state by ID.
    /// </summary>
    /// <param name="instanceId">ID of orchestration to purge.</param>
    /// <returns>The purge result.</returns>
    public PurgeResult PurgeInstanceState(string instanceId)
    {
        if (instanceId != null && this.store.TryGetValue(instanceId, out SerializedInstanceState? state) && state.IsCompleted)
        {
            this.store.TryRemove(instanceId, out SerializedInstanceState? removedState);
            if (removedState != null)
            {
                return new PurgeResult(1);
            }
        }

        return new PurgeResult(0);
    }

    /// <summary>
    /// Purge orchestrations via a filter.
    /// </summary>
    /// <param name="purgeInstanceFilter">Filter to purge with.</param>
    /// <returns>The purge result.</returns>
    public PurgeResult PurgeInstanceState(PurgeInstanceFilter purgeInstanceFilter)
    {
        int counter = 0;

        List<string> filteredInstanceIds = this.store
            .Where(item =>
            {
                OrchestrationState? statusRecord = item.Value.StatusRecordJson?.GetValue<OrchestrationState>();
                if (statusRecord == null)
                {
                    return false;
                }

                if (purgeInstanceFilter.CreatedTimeFrom > statusRecord.CreatedTime)
                {
                    return false;
                }

                if (purgeInstanceFilter.CreatedTimeTo != null
                    && purgeInstanceFilter.CreatedTimeTo < statusRecord.CreatedTime)
                {
                    return false;
                }

                if (purgeInstanceFilter.RuntimeStatus != null && purgeInstanceFilter.RuntimeStatus.Any()
                    && !purgeInstanceFilter.RuntimeStatus.Contains(statusRecord.OrchestrationStatus))
                {
                    return false;
                }

                return true;
            })
            .Select(item => item.Key)
            .ToList();

        foreach (string instanceId in filteredInstanceIds)
        {
            this.store.TryRemove(instanceId, out SerializedInstanceState? removedState);
            if (removedState != null)
            {
                counter++;
            }
        }

        return new PurgeResult(counter);
    }

    class ReadyToRunQueue
    {
        readonly Channel<SerializedInstanceState> readyToRunQueue = Channel.CreateUnbounded<SerializedInstanceState>();
        readonly Dictionary<string, object> readyInstances = new(StringComparer.OrdinalIgnoreCase);

        public void Reset()
        {
            this.readyInstances.Clear();
        }

        public async ValueTask<SerializedInstanceState> TakeNextAsync(CancellationToken ct)
        {
            while (true)
            {
                SerializedInstanceState state = await this.readyToRunQueue.Reader.ReadAsync(ct);
                lock (state)
                {
                    if (this.readyInstances.Remove(state.InstanceId))
                    {
                        if (state.IsLoaded)
                        {
                            throw new InvalidOperationException("Should never load state that is already loaded.");
                        }

                        state.IsLoaded = true;
                        return state;
                    }
                }
            }
        }

        public void Schedule(SerializedInstanceState state)
        {
            // TODO: There is a race condition here. If another thread is calling TakeNextAsync
            //       and removed the queue item before updating the dictionary, then we'll fail
            //       to update the readyToRunQueue and the orchestration will get stuck.
            if (this.readyInstances.TryAdd(state.InstanceId, state))
            {
                this.readyToRunQueue.Writer.TryWrite(state);
            }
        }
    }

    class SerializedInstanceState
    {
        public SerializedInstanceState(string instanceId, string? executionId)
        {
            this.InstanceId = instanceId;
            this.ExecutionId = executionId;
        }

        public string InstanceId { get; }

        public string? ExecutionId { get; internal set; }

        public JsonValue? StatusRecordJson { get; set; }

        public JsonArray HistoryEventsJson { get; } = new JsonArray();

        public JsonArray MessagesJson { get; } = new JsonArray();

        internal bool IsLoaded { get; set; }

        internal bool IsCompleted { get; set; }
    }
}
