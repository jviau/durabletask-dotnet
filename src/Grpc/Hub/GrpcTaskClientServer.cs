// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.DurableTask.Protobuf.Experimental;
using C = DurableTask.Core;
using H = DurableTask.Core.History;
using P = Microsoft.DurableTask.Protobuf.Experimental;

namespace Microsoft.DurableTask.Grpc.Hub;

/// <summary>
/// Implementation of DurableTaskClientBase.
/// </summary>
public class GrpcTaskClientServer : DurableTaskClient.DurableTaskClientBase
{
    readonly IOrchestrationServiceClient client;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcTaskClientServer"/> class.
    /// </summary>
    /// <param name="client">The orchestration service client.</param>
    public GrpcTaskClientServer(IOrchestrationServiceClient client)
    {
        this.client = Check.NotNull(client);
    }

    /// <inheritdoc/>
    public override async Task<OrchestrationInfoResponse> ScheduleOrchestration(
        ScheduleOrchestrationRequest request, ServerCallContext context)
    {
        Check.NotNull(request);
        Check.NotNull(context);

        string instanceId = request.Options?.InstanceId ?? Guid.NewGuid().ToString("N");
        OrchestrationInstance instance = new()
        {
            InstanceId = instanceId,
            ExecutionId = Guid.NewGuid().ToString(),
        };

        TaskMessage message = new()
        {
            Event = new H.ExecutionStartedEvent(-1, request.Input)
            {
                Name = request.Name.Name,
                Version = request.Name.Version,
                OrchestrationInstance = instance,
            },
            OrchestrationInstance = instance,
        };

        await this.client.CreateTaskOrchestrationAsync(message);
        C.OrchestrationState state = await this.client.GetOrchestrationStateAsync(instanceId, instance.ExecutionId);
        return state.ToResponse();
    }

    /// <inheritdoc/>
    public override async Task<OrchestrationInfoResponse> GetOrchestration(
        GetOrchestrationRequest request, ServerCallContext context)
    {
        Check.NotNull(request);
        Check.NotNull(context);

        string id = request.InstanceId;
        OrchestrationExpandDetail expand = OrchestrationExpandDetailExtensions.FromProto(request.Expand);
        C.OrchestrationState? state = await this.client.GetOrchestrationStateAsync(id, executionId: null);
        return state is null
            ? throw new RpcException(
                new(StatusCode.NotFound, $"Orchestration with instance ID {id} does not exist."))
            : state.ToResponse(expand);
    }

    /// <inheritdoc/>
    public override async Task<OrchestrationInfoResponse> WaitForOrchestrationState(
        WaitForOrchestrationRequest request, ServerCallContext context)
    {
        Check.NotNull(request);
        Check.NotNull(context);

        static bool BuildStates(ICollection<P.OrchestrationState> input, out HashSet<C.OrchestrationStatus>? output)
        {
            if (input.Count == 0)
            {
                output = null;
                return true;
            }

            bool terminal = true;
            HashSet<C.OrchestrationStatus>? states = null;
            foreach (P.OrchestrationState state in input)
            {
                if (!state.IsTerminal())
                {
                    terminal = false;
                }

                states ??= new HashSet<C.OrchestrationStatus>();
                states.Add(state.Convert());
            }

            output = states;
            return terminal;
        }

        Task<C.OrchestrationState> WaitForTerminalAsync(string instanceId, CancellationToken cancellation)
        {
            return this.client.WaitForOrchestrationAsync(
                instanceId, executionId: null, timeout: Timeout.InfiniteTimeSpan, cancellation);
        }

        async Task<C.OrchestrationState> WaitForStateAsync(
            string instanceId, HashSet<C.OrchestrationStatus> states, CancellationToken cancellation)
        {
            while (true)
            {
                C.OrchestrationState state = await this.client.GetOrchestrationStateAsync(
                    instanceId, executionId: null);

                // If state is not null and is either terminal or one of our desired states we will return.
                // We always check for terminal as it will never reach the desired stat.
                if (state != null && (state.OrchestrationStatus.IsTerminal()
                    || states.Contains(state.OrchestrationStatus)))
                {
                    return state;
                }

                // TODO: Backoff strategy if we're delaying for a long time.
                // The cancellation token is what will break us out of this loop if the orchestration
                // never leaves the "Pending" state.
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellation);
            }
        }

        C.OrchestrationState? state;
        if (BuildStates(request.States, out HashSet<C.OrchestrationStatus>? target))
        {
            state = await WaitForTerminalAsync(request.InstanceId, context.CancellationToken);
        }
        else
        {
            state = await WaitForStateAsync(request.InstanceId, target!, context.CancellationToken);
        }

        OrchestrationExpandDetail expand = OrchestrationExpandDetailExtensions.FromProto(request.Expand);
        return state.ToResponse(expand);
    }

    /// <inheritdoc/>
    public override async Task<Empty> RaiseOrchestrationEvent(RaiseEventRequest request, ServerCallContext context)
    {
        Check.NotNull(request);
        Check.NotNull(context);

        if (request.InstanceId is null or "")
        {
            throw new RpcException(new(
                StatusCode.InvalidArgument, $"Non null or empty instance ID must be supplied."));
        }

        TaskMessage message = new()
        {
            OrchestrationInstance = new() { InstanceId = request.InstanceId },
            Event = new H.EventRaisedEvent(-1, request.Input) { Name = request.Name },
        };

        await this.client.SendTaskOrchestrationMessageAsync(message).WaitAsync(context.CancellationToken);
        return new();
    }

    /// <inheritdoc/>
    public override async Task<OrchestrationInfoResponse> TerminateOrchestration(
        TerminateOrchestrationRequest request, ServerCallContext context)
    {
        Check.NotNull(request);
        Check.NotNull(context);

        if (request.InstanceId is null or "")
        {
            throw new RpcException(new(
                StatusCode.InvalidArgument, $"Non null or empty instance ID must be supplied."));
        }

        string id = request.InstanceId;
        await this.client.ForceTerminateTaskOrchestrationAsync(id, request.Reason);
        return new();
    }
}
