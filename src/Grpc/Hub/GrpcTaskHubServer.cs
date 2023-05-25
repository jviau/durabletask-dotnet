// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Threading.Channels;
using DurableTask.Core;
using DurableTask.Core.History;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.DurableTask.Protobuf.Experimental;
using Microsoft.Extensions.Hosting;
using C = DurableTask.Core;

namespace Microsoft.DurableTask.Grpc.Hub;

/// <summary>
/// Implementation of DurableTaskHubBase. If registered to a service container, this <b>must</b> be a singleton. Or at
/// least the same instance must be shared for connections from the same client.
/// </summary>
public sealed class GrpcTaskHubServer : DurableTaskHub.DurableTaskHubBase, IAsyncDisposable
{
    readonly object sync = new();
    readonly CancellationTokenSource cts;
    readonly IOrchestrationService orchestrationService;
    readonly ConcurrentDictionary<string, TaskOrchestrationWorkItem> pendingOrchestrations = new();
    readonly ConcurrentDictionary<string, TaskActivityWorkItem> pendingActivities = new();
    readonly AsyncManualResetEvent readersAvailable = new(set: false);
    readonly Channel<WorkItem> workQueue = Channel.CreateBounded<WorkItem>(100);

    TaskCompletionSource? disposal;
    int readers;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcTaskHubServer" /> class.
    /// </summary>
    /// <param name="lifetime">The host lifetime.</param>
    /// <param name="orchestrationService">The orchestration service.</param>
    public GrpcTaskHubServer(IHostApplicationLifetime lifetime, IOrchestrationService orchestrationService)
    {
        Check.NotNull(lifetime);
        Check.NotNull(orchestrationService);

        this.cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
        this.orchestrationService = orchestrationService;
        _ = Task.Factory.StartNew(this.DequeueLoopAsync);
    }

    CancellationToken ShutdownToken => this.cts.Token;

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        async Task DisposeCoreAsync()
        {
            try
            {
                foreach (string key in this.pendingOrchestrations.Keys)
                {
                    try
                    {
                        if (this.pendingOrchestrations.TryRemove(key, out TaskOrchestrationWorkItem? item))
                        {
                            await this.orchestrationService.AbandonTaskOrchestrationWorkItemAsync(item);
                            await this.orchestrationService.ReleaseTaskOrchestrationWorkItemAsync(item);
                        }
                    }
                    catch
                    {
                    }
                }

                foreach (string key in this.pendingActivities.Keys)
                {
                    try
                    {
                        if (this.pendingActivities.TryRemove(key, out TaskActivityWorkItem? item))
                        {
                            await this.orchestrationService.AbandonTaskActivityWorkItemAsync(item);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                this.disposal.TrySetResult();
            }
        }

        bool dispose = false;
        lock (this.sync)
        {
            if (this.disposal is null)
            {
                dispose = true;

                if (!this.cts.IsCancellationRequested)
                {
                    this.cts.Cancel();
                }

                this.disposal = new();
            }
        }

        this.cts.Dispose();
        if (dispose)
        {
            return new(DisposeCoreAsync());
        }

        return new(this.disposal.Task);
    }

    /// <inheritdoc/>
    public override async Task WorkItemStream(
        GetWorkItemsRequest request, IServerStreamWriter<WorkItem> responseStream, ServerCallContext context)
    {
        Check.NotNull(request);
        Check.NotNull(responseStream);
        Check.NotNull(context);

        try
        {
            if (Interlocked.Increment(ref this.readers) == 1)
            {
                this.readersAvailable.Set();
            }

            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(
                this.cts.Token, context.CancellationToken);
            await foreach (WorkItem item in this.workQueue.Reader.ReadAllAsync(cts.Token))
            {
                await responseStream.WriteAsync(item, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // client disconnected
        }
        finally
        {
            if (Interlocked.Decrement(ref this.readers) == 0)
            {
                this.readersAvailable.Reset();
            }
        }
    }

    /// <inheritdoc/>
    public override async Task OrchestrationStream(
        IAsyncStreamReader<OrchestratorAction> requestStream,
        IServerStreamWriter<OrchestratorMessage> responseStream,
        ServerCallContext context)
    {
        Check.NotNull(requestStream);
        Check.NotNull(responseStream);
        Check.NotNull(context);

        CancellationToken cancellation = context.CancellationToken;

        try
        {
            // Clients must first send a message indicating which orchestration they want to stream.
            if (!await requestStream.MoveNext(cancellation))
            {
                return;
            }

            if (requestStream.Current is not { Start: { } start })
            {
                throw new RpcException(new(StatusCode.InvalidArgument, "Callers must start with a StartStreamEvent."));
            }

            if (!this.pendingOrchestrations.TryRemove(start.InstanceId, out TaskOrchestrationWorkItem? orchestration))
            {
                throw new RpcException(
                    new(StatusCode.NotFound, $"Orchestration with ID {start.InstanceId} is not available."));
            }

            try
            {
                Task write = WriteEventsAsync(orchestration.OrchestrationRuntimeState!, responseStream, cancellation);
                Task<OrchestratorActionCollection> read = ReadEventsAsync(requestStream, cancellation);

                await Task.WhenAll(write, read);
                OrchestratorActionCollection result = await read;
                await this.CompleteOrchestrationAsync(orchestration, result, cancellation);
            }
            catch
            {
                await this.orchestrationService.AbandonTaskOrchestrationWorkItemAsync(orchestration);
            }
            finally
            {
                await this.orchestrationService.ReleaseTaskOrchestrationWorkItemAsync(orchestration);
            }
        }
        catch (OperationCanceledException)
        {
            // client disconnected
        }
    }

    /// <inheritdoc/>
    public override async Task<Empty> CompleteTaskActivity(ActivityResult request, ServerCallContext context)
    {
        Check.NotNull(request);
        Check.NotNull(context);

        CancellationToken cancellation = context.CancellationToken;
        string key = GetActivityKey(request.Parent.InstanceId, request.TaskId);
        if (!this.pendingActivities.TryRemove(key, out TaskActivityWorkItem? workItem))
        {
            throw new RpcException(
                new(StatusCode.NotFound, $"Activity with ID {key} is not available."));
        }

        HistoryEvent result = request switch
        {
            { Error: { } error } => new TaskFailedEvent(-1, request.TaskId, null, null, error.ToFailure()),
            _ => new TaskCompletedEvent(-1, request.TaskId, request.Result),
        };

        TaskMessage message = new()
        {
            Event = result,
            OrchestrationInstance = workItem.TaskMessage.OrchestrationInstance,
        };

        cancellation.ThrowIfCancellationRequested();
        await this.orchestrationService.CompleteTaskActivityWorkItemAsync(workItem, message);
        return new();
    }

    static async Task WriteEventsAsync(
        OrchestrationRuntimeState start,
        IServerStreamWriter<OrchestratorMessage> writer,
        CancellationToken cancellation)
    {
        foreach (HistoryEvent history in start.PastEvents)
        {
            if (history.ToMessage() is { } m)
            {
                await writer.WriteAsync(m, cancellation);
            }
        }

        // We send this event to let implementations know when "replaying" mode is done.
        await writer.WriteAsync(
            new OrchestratorMessage()
            {
                Id = -1,
                Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
                Resumed = new Protobuf.Experimental.ExecutionResumedEvent(),
            },
            cancellation);

        foreach (HistoryEvent history in start.NewEvents)
        {
            if (history.ToMessage() is { } m)
            {
                await writer.WriteAsync(m, cancellation);
            }
        }

        // We tell the client there will be no more messages on this stream, they should disconnect.
        await writer.WriteAsync(
            new OrchestratorMessage()
            {
                Id = -1,
                Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
                Disconnect = new(),
            },
            cancellation);
    }

    static async Task<OrchestratorActionCollection> ReadEventsAsync(
        IAsyncStreamReader<OrchestratorAction> reader, CancellationToken cancellation)
    {
        // TODO: do we need to catch cancellations and publish what we have so far?
        OrchestratorActionCollection result = new();
        await foreach (OrchestratorAction? action in reader.ReadAllAsync(cancellation))
        {
            if (action is null)
            {
                continue;
            }

            result.Add(action);
        }

        return result;
    }

    static string GetActivityKey(string instanceId, int taskId) => $"{instanceId}.{taskId}";

    async Task CompleteOrchestrationAsync(
        TaskOrchestrationWorkItem workItem,
        OrchestratorActionCollection result,
        CancellationToken cancellation)
    {
        result.ApplyActions(
            ref workItem.OrchestrationRuntimeState,
            out IList<TaskMessage> activityMessages,
            out IList<TaskMessage> orchestratorMessages,
            out IList<TaskMessage> timerMessages,
            out C.OrchestrationState? updatedStatus,
            out bool continueAsNew);

        if (continueAsNew)
        {
            // Instead of sending back to the orchestration service, we will renew lock if necessary then immediately
            // re-queue this to connected workers to consume.
            if (workItem.LockedUntilUtc != default && DateTime.UtcNow.AddMinutes(1) > workItem.LockedUntilUtc)
            {
                await this.orchestrationService.RenewTaskOrchestrationWorkItemLockAsync(workItem);
            }

            // Re-enqueue this right away.
            await this.EnqueueAsync(workItem, cancellation);
            return;
        }

        await this.orchestrationService.CompleteTaskOrchestrationWorkItemAsync(
            workItem,
            workItem.OrchestrationRuntimeState,
            activityMessages,
            orchestratorMessages,
            timerMessages,
            continuedAsNewMessage: null,
            updatedStatus);
    }

    async Task DequeueLoopAsync()
    {
        try
        {
            await this.readersAvailable.WaitAsync(this.ShutdownToken);
            await this.orchestrationService.StartAsync();
            await Task.WhenAll(
                this.DequeueActivitiesAsync(this.ShutdownToken),
                this.DequeueOrchestrationsAsync(this.ShutdownToken));
        }
        catch (Exception ex)
        {
            this.workQueue.Writer.TryComplete(ex);
            throw;
        }

        this.workQueue.Writer.TryComplete();
    }

    async Task DequeueActivitiesAsync(CancellationToken cancellation)
    {
        while (await this.workQueue.Writer.WaitToWriteAsync(cancellation))
        {
            await this.readersAvailable.WaitAsync(cancellation);
            TaskActivityWorkItem activity = await this.orchestrationService
                .LockNextTaskActivityWorkItem(Timeout.InfiniteTimeSpan, cancellation);

            if (activity is null)
            {
                continue;
            }

            TaskScheduledEvent @event = (TaskScheduledEvent)activity.TaskMessage.Event;

            OrchestrationInstance instance = activity.TaskMessage.OrchestrationInstance;
            WorkItem workItem = new()
            {
                Activity = new()
                {
                    Id = @event.EventId,
                    Name = new()
                    {
                        Name = @event.Name,
                        Version = @event.Version,
                    },
                    Parent = new()
                    {
                        InstanceId = instance.InstanceId,
                        ExecutionId = instance.ExecutionId,
                    },
                    Input = @event.Input,
                },
            };

            string key = GetActivityKey(instance.InstanceId, @event.EventId);
            try
            {
                this.pendingActivities.TryAdd(key, activity);
                await this.workQueue.Writer.WriteAsync(workItem, cancellation);
            }
            catch
            {
                // swallow errors.
                this.pendingActivities.TryRemove(key, out _);
                await this.orchestrationService.AbandonTaskActivityWorkItemAsync(activity);
            }
        }
    }

    async Task DequeueOrchestrationsAsync(CancellationToken cancellation)
    {
        while (await this.workQueue.Writer.WaitToWriteAsync(cancellation))
        {
            await this.readersAvailable.WaitAsync(cancellation);
            TaskOrchestrationWorkItem orchestration = await this.orchestrationService
                .LockNextTaskOrchestrationWorkItemAsync(Timeout.InfiniteTimeSpan, cancellation);
            if (orchestration is null)
            {
                continue;
            }

            await this.EnqueueAsync(orchestration, cancellation);
        }
    }

    async ValueTask EnqueueAsync(TaskOrchestrationWorkItem orchestration, CancellationToken cancellation)
    {
        orchestration.OrchestrationRuntimeState.AddEvent(new OrchestratorStartedEvent(-1));
        foreach (TaskMessage message in orchestration.FilterAndSortMessages())
        {
            orchestration.OrchestrationRuntimeState.AddEvent(message.Event);
        }

        OrchestrationRuntimeState state = orchestration.OrchestrationRuntimeState!;
        OrchestrationInstance instance = state.OrchestrationInstance!;
        WorkItem workItem = new()
        {
            Orchestrator = new()
            {
                Id = new()
                {
                    InstanceId = instance.InstanceId,
                    ExecutionId = instance.ExecutionId,
                },
                Name = new()
                {
                    Name = state.Name,
                    Version = state.Version,
                },
            },
        };

        if (state.ParentInstance is { } parent)
        {
            workItem.Orchestrator.Parent = new()
            {
                Id = new()
                {
                    InstanceId = parent.OrchestrationInstance.InstanceId,
                    ExecutionId = parent.OrchestrationInstance.ExecutionId,
                },
                Name = new()
                {
                    Name = parent.Name,
                    Version = parent.Version,
                },
                ScheduledId = parent.TaskScheduleId,
            };
        }

        if (state.Tags is { } tags)
        {
            foreach ((string key, string value) in tags)
            {
                workItem.Orchestrator.Metadata.Add(key, value);
            }
        }

        try
        {
            this.pendingOrchestrations.TryAdd(instance.InstanceId, orchestration);
            await this.workQueue.Writer.WriteAsync(workItem, cancellation);
        }
        catch
        {
            // swallow errors.
            this.pendingOrchestrations.TryRemove(instance.InstanceId, out _);
            await this.orchestrationService.AbandonTaskOrchestrationWorkItemAsync(orchestration);
            await this.orchestrationService.ReleaseTaskOrchestrationWorkItemAsync(orchestration);
        }
    }
}
