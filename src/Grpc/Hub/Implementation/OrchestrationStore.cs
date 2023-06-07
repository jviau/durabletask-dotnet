// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using DurableTask.Core;
using DurableTask.Core.History;
using DurableTask.Core.Query;

namespace Microsoft.DurableTask.Grpc.Hub.Implementation;

/// <summary>
/// Queue for orchestration work items.
/// </summary>
class OrchestrationStore
{
    readonly object sync = new();
    Channel<InMemoryOrchestration> readyToRun;
    ConcurrentDictionary<string, InMemoryOrchestration> orchestrations;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestrationStore"/> class.
    /// </summary>
    public OrchestrationStore()
    {
        this.Reset();
    }

    /// <summary>
    /// Resets this store.
    /// </summary>
    [MemberNotNull(nameof(readyToRun), nameof(orchestrations))]
    public void Reset()
    {
        lock (this.sync)
        {
            this.readyToRun = Channel.CreateUnbounded<InMemoryOrchestration>();
            this.orchestrations = new(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Dequeue an orchestration.
    /// </summary>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task which contains the dequeued orchestration.</returns>
    public ValueTask<InMemoryOrchestration> DequeueAsync(CancellationToken cancellation = default)
    {
        return this.readyToRun.Reader.ReadAsync(cancellation);
    }

    /// <summary>
    /// Delivers a message to an orchestration.
    /// </summary>
    /// <param name="message">The task message to deliver.</param>
    public void DeliverMessage(TaskMessage message)
    {
        Check.NotNull(message);

        static bool TryGetDeliveryDelay(TaskMessage message, out TimeSpan delay)
        {
            DateTimeOffset? target = message.Event switch
            {
                ExecutionStartedEvent e => e.ScheduledStartTime,
                TimerFiredEvent e => e.FireAt,
                _ => null,
            };

            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (target.HasValue && target.Value < now)
            {
                delay = default;
                return false;
            }

            delay = target.HasValue ? target.Value - now : default;
            return target.HasValue;
        }

        if (TryGetDeliveryDelay(message, out TimeSpan delay))
        {
            Task.Run(async () =>
            {
                await Task.Delay(delay);
                this.DeliverMessage(message);
            }).Forget();
            return;
        }

        InMemoryOrchestration orchestration = this.orchestrations.GetOrAdd(
            message.OrchestrationInstance.InstanceId,
            (id, writer) => new InMemoryOrchestration(id, writer),
            this.readyToRun.Writer);
        orchestration.AddMessageAsync(message).Forget();
    }

    /// <summary>
    /// Tries to get the state for an orchestration instance.
    /// </summary>
    /// <param name="instanceId">The instance to get state for.</param>
    /// <param name="state">The found state.</param>
    /// <returns><c>true</c> if found, <c>false</c> otherwise.</returns>
    public bool TryGetState(string instanceId, [NotNullWhen(true)] out OrchestrationState? state)
    {
        Check.NotNull(instanceId);
        if (this.orchestrations.TryGetValue(instanceId, out InMemoryOrchestration? value))
        {
            state = value.State;
            return true;
        }

        state = null;
        return false;
    }

    /// <summary>
    /// Tries to remove an orchestration by ID from the store.
    /// </summary>
    /// <param name="instanceId">The instance ID to remove.</param>
    /// <returns><c>true</c> if removed, <c>false</c> otherwise.</returns>
    public bool TryRemove(string instanceId)
    {
        Check.NotNull(instanceId);
        return this.orchestrations.TryRemove(instanceId, out _);
    }

    /// <summary>
    /// Removes all orchestrations from the store described by the provided filter.
    /// </summary>
    /// <param name="filter">The filter defining which instances to remove.</param>
    /// <returns>The count of orchestrations removed.</returns>
    public int RemoveAll(PurgeInstanceFilter filter)
    {
        Check.NotNull(filter);

        int removed = 0;
        InMemoryOrchestration.Query query = filter;
        foreach (InMemoryOrchestration orchestration in this.orchestrations.Values)
        {
            if (orchestration.Matches(query) && this.orchestrations.TryRemove(orchestration.Id, out _))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// Gets all orchestrations which match a provided query.
    /// </summary>
    /// <param name="query">The query to match orchestrations against.</param>
    /// <param name="continuation">The continuation token.</param>
    /// <returns>All orchestrations which matched the query.</returns>
    public IReadOnlyCollection<OrchestrationState> GetAll(OrchestrationQuery query, out string? continuation)
    {
        Check.NotNull(query);

        int start = 0;
        continuation = null;
        if (query.ContinuationToken is { } token && !int.TryParse(token, out start))
        {
            throw new ArgumentException("ContinuationToken cannot be parsed as an integer.", nameof(query));
        }

        int counter = 0;
        int size = Math.Min(Math.Max(this.orchestrations.Count - start, 0), query.PageSize);
        if (size == 0)
        {
            return Array.Empty<OrchestrationState>();
        }

        List<OrchestrationState> results = new(size);
        InMemoryOrchestration.Query q = query;
        foreach (InMemoryOrchestration orchestration in this.orchestrations.Values.Skip(start))
        {
            start++;
            if (orchestration.Matches(q))
            {
                counter++;
                results.Add(orchestration.State);
            }

            if (counter >= query.PageSize)
            {
                continuation = start.ToStringInvariant();
                break;
            }
        }

        return results;
    }

    /// <summary>
    /// Waits for an orhcestration instance to complete.
    /// </summary>
    /// <param name="instanceId">The instance to wait on.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>The state of the completed instance, or null if no instance found.</returns>
    public async Task<OrchestrationState?> WaitAsync(
        string instanceId, CancellationToken cancellation = default)
    {
        Check.NotNull(instanceId);
        if (!this.orchestrations.TryGetValue(instanceId, out InMemoryOrchestration? value))
        {
            return null;
        }

        await value.Completion.WaitAsync(cancellation);
        return value.State;
    }
}
