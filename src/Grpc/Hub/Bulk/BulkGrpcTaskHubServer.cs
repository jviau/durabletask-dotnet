// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Threading.Channels;
using DurableTask.Core;
using DurableTask.Core.History;
using Microsoft.Extensions.Hosting;
using static Microsoft.DurableTask.Protobuf.TaskHubSidecarService;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Grpc.Hub.Bulk;

/// <summary>
/// Implementation of the gRPC contract <see cref="TaskHubSidecarServiceBase"/>.
/// </summary>
public sealed partial class BulkGrpcTaskHubServer : TaskHubSidecarServiceBase, IAsyncDisposable
{
    readonly object sync = new();
    readonly CancellationTokenSource cts;
    readonly IOrchestrationService service;
    readonly IOrchestrationServiceClient client;
    readonly ConcurrentDictionary<string, TaskOrchestrationWorkItem> pendingOrchestrations = new();
    readonly ConcurrentDictionary<string, TaskActivityWorkItem> pendingActivities = new();
    readonly AsyncManualResetEvent readersAvailable = new(set: false);
    readonly Channel<P.WorkItem> workQueue = Channel.CreateBounded<P.WorkItem>(100);

    TaskCompletionSource? disposal;

    /// <summary>
    /// Initializes a new instance of the <see cref="BulkGrpcTaskHubServer"/> class.
    /// </summary>
    /// <param name="lifetime">The host lifetime.</param>
    /// <param name="service">The orchestration service.</param>
    /// <param name="client">The orchestration service client.</param>
    public BulkGrpcTaskHubServer(
        IHostApplicationLifetime lifetime,
        IOrchestrationService service,
        IOrchestrationServiceClient client)
    {
        Check.NotNull(lifetime);
        Check.NotNull(service);

        this.cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
        this.service = service;
        this.client = client;
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
                            await this.service.AbandonTaskOrchestrationWorkItemAsync(item);
                            await this.service.ReleaseTaskOrchestrationWorkItemAsync(item);
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
                            await this.service.AbandonTaskActivityWorkItemAsync(item);
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

    static string GetActivityKey(string instanceId, int taskId) => $"{instanceId}.{taskId}";

    async Task DequeueLoopAsync()
    {
        try
        {
            await this.readersAvailable.WaitAsync(this.ShutdownToken);
            await this.service.StartAsync();
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
            TaskActivityWorkItem activity = await this.service
                .LockNextTaskActivityWorkItem(Timeout.InfiniteTimeSpan, cancellation);

            if (activity is null)
            {
                continue;
            }

            TaskScheduledEvent @event = (TaskScheduledEvent)activity.TaskMessage.Event;

            OrchestrationInstance instance = activity.TaskMessage.OrchestrationInstance;
            P.WorkItem workItem = new()
            {
                ActivityRequest = new()
                {
                    OrchestrationInstance = new()
                    {
                        InstanceId = instance.InstanceId,
                        ExecutionId = instance.ExecutionId,
                    },
                    Name = @event.Name,
                    Version = @event.Version,
                    Input = @event.Input,
                    TaskId = @event.EventId,
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
                await this.service.AbandonTaskActivityWorkItemAsync(activity);
            }
        }
    }

    async Task DequeueOrchestrationsAsync(CancellationToken cancellation)
    {
        while (await this.workQueue.Writer.WaitToWriteAsync(cancellation))
        {
            await this.readersAvailable.WaitAsync(cancellation);
            TaskOrchestrationWorkItem orchestration = await this.service
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
        P.WorkItem workItem = new()
        {
            OrchestratorRequest = new()
            {
                InstanceId = instance.InstanceId,
                ExecutionId = instance.ExecutionId,
                NewEvents = { state.NewEvents.Select(x => x.ToHistoryEventProto()) },
                PastEvents = { state.PastEvents.Select(x => x.ToHistoryEventProto()) },
            },
        };

        try
        {
            this.pendingOrchestrations.TryAdd(instance.InstanceId, orchestration);
            await this.workQueue.Writer.WriteAsync(workItem, cancellation);
        }
        catch
        {
            // swallow errors.
            this.pendingOrchestrations.TryRemove(instance.InstanceId, out _);
            await this.service.AbandonTaskOrchestrationWorkItemAsync(orchestration);
            await this.service.ReleaseTaskOrchestrationWorkItemAsync(orchestration);
        }
    }
}
