// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.History;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using static Microsoft.DurableTask.Protobuf.TaskHubSidecarService;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Grpc.Hub.Bulk;

/// <summary>
/// Implementation of the gRPC contract <see cref="TaskHubSidecarServiceBase"/>.
/// </summary>
partial class BulkGrpcTaskHubServer
{
    int readers;

    /// <inheritdoc/>
    public override Task<Empty> Hello(Empty request, ServerCallContext context) => Task.FromResult(new Empty());

    /// <inheritdoc/>
    public override async Task GetWorkItems(
        P.GetWorkItemsRequest request, IServerStreamWriter<P.WorkItem> responseStream, ServerCallContext context)
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
                this.ShutdownToken, context.CancellationToken);
            await foreach (P.WorkItem item in this.workQueue.Reader.ReadAllAsync(cts.Token))
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
    public override async Task<P.CompleteTaskResponse> CompleteActivityTask(
        P.ActivityResponse request, ServerCallContext context)
    {
        Check.NotNull(request);
        Check.NotNull(context);

        CancellationToken cancellation = context.CancellationToken;
        string key = GetActivityKey(request.InstanceId, request.TaskId);
        if (!this.pendingActivities.TryRemove(key, out TaskActivityWorkItem? workItem))
        {
            throw new RpcException(
                new(StatusCode.NotFound, $"Activity with ID {key} is not available."));
        }

        HistoryEvent result = request switch
        {
            { FailureDetails: { } error } => new TaskFailedEvent(
                -1, request.TaskId, null, null, error.GetFailureDetails()),
            _ => new TaskCompletedEvent(-1, request.TaskId, request.Result),
        };

        TaskMessage message = new()
        {
            Event = result,
            OrchestrationInstance = workItem.TaskMessage.OrchestrationInstance,
        };

        cancellation.ThrowIfCancellationRequested();
        await this.service.CompleteTaskActivityWorkItemAsync(workItem, message);
        return new();
    }

    /// <inheritdoc/>
    public override async Task<P.CompleteTaskResponse> CompleteOrchestratorTask(
        P.OrchestratorResponse request, ServerCallContext context)
    {
        Check.NotNull(request);
        Check.NotNull(context);
        CancellationToken cancellation = context.CancellationToken;
        if (!this.pendingOrchestrations.TryRemove(request.InstanceId, out TaskOrchestrationWorkItem? workItem))
        {
            throw new RpcException(
                new(StatusCode.NotFound, $"Orchestration with ID {request.InstanceId} is not available."));
        }

        try
        {
            OrchestratorExecutionResult result = new()
            {
                Actions = request.Actions.Select(x => x.ToOrchestratorAction()),
                CustomStatus = request.CustomStatus,
            };

            result.ApplyActions(
                ref workItem.OrchestrationRuntimeState,
                out IList<TaskMessage> activityMessages,
                out IList<TaskMessage> orchestratorMessages,
                out IList<TaskMessage> timerMessages,
                out OrchestrationState? updatedStatus,
                out bool continueAsNew);

            if (continueAsNew)
            {
                // Continue running the orchestration with a new history.
                // Renew the lock if we're getting close to its expiration.
                if (workItem.LockedUntilUtc != default && DateTime.UtcNow.AddMinutes(1) > workItem.LockedUntilUtc)
                {
                    await this.service.RenewTaskOrchestrationWorkItemLockAsync(workItem);
                }

                await this.EnqueueAsync(workItem, cancellation);
                return new();
            }

            // Commit the changes to the durable store
            await this.service.CompleteTaskOrchestrationWorkItemAsync(
                workItem,
                workItem.OrchestrationRuntimeState,
                activityMessages,
                orchestratorMessages,
                timerMessages,
                continuedAsNewMessage: null /* not supported */,
                updatedStatus);

            return new();
        }
        catch
        {
            await this.service.AbandonTaskOrchestrationWorkItemAsync(workItem);
            throw;
        }
        finally
        {
            await this.service.ReleaseTaskOrchestrationWorkItemAsync(workItem);
        }
    }
}
