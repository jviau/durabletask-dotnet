// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.DurableTask.Protobuf.Experimental;
using C = DurableTask.Core;
using H = DurableTask.Core.History;

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

        await this.client.SendTaskOrchestrationMessageAsync(message);
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

    /// <inheritdoc/>
    public override async Task<PurgeOrchestrationsResponse> PurgeOrchestrations(
        PurgeOrchestrationsRequest request, ServerCallContext context)
    {
        Check.NotNull(request);
        Check.NotNull(context);

        static PurgeInstanceFilter Convert(PurgeFilter filter)
        {
            IEnumerable<C.OrchestrationStatus>? statuses = filter.IncludeStates?.Select(x => x.Convert()).ToList();
            return new(filter.CreatedFrom?.ToDateTime() ?? DateTime.MinValue, filter.CreatedTo?.ToDateTime(), statuses);
        }

        IOrchestrationServicePurgeClient purgeClient = (IOrchestrationServicePurgeClient)this.client;
        Task<PurgeResult> task = request switch
        {
            { Filter: { } filter } => purgeClient.PurgeInstanceStateAsync(Convert(filter)),
            { InstanceId: { } id } => purgeClient.PurgeInstanceStateAsync(id),
        };

        PurgeResult result = await task;
        return new PurgeOrchestrationsResponse()
        {
            PurgedCount = result.DeletedInstanceCount,
            ContinuationToken = null,
        };
    }
}
