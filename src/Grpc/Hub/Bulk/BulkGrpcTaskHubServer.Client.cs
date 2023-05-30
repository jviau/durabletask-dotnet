// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.History;
using DurableTask.Core.Query;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using static Microsoft.DurableTask.Protobuf.TaskHubSidecarService;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Grpc.Hub.Bulk;

/// <summary>
/// Implementation of the gRPC contract <see cref="TaskHubSidecarServiceBase"/>.
/// </summary>
public partial class BulkGrpcTaskHubServer
{
    /// <inheritdoc/>
    public override async Task<P.CreateInstanceResponse> StartInstance(
        P.CreateInstanceRequest request, ServerCallContext context)
    {
        Check.NotNull(request);
        Check.NotNull(context);
        OrchestrationInstance instance = new()
        {
            InstanceId = request.InstanceId ?? Guid.NewGuid().ToString("N"),
            ExecutionId = Guid.NewGuid().ToString(),
        };

        await this.client.CreateTaskOrchestrationAsync(
            new TaskMessage
            {
                Event = new ExecutionStartedEvent(-1, request.Input)
                {
                    Name = request.Name,
                    Version = request.Version,
                    OrchestrationInstance = instance,
                },
                OrchestrationInstance = instance,
            });

        return new P.CreateInstanceResponse
        {
            InstanceId = instance.InstanceId,
        };
    }

    /// <inheritdoc/>
    public override async Task<P.GetInstanceResponse> GetInstance(
        P.GetInstanceRequest request, ServerCallContext context)
    {
        Check.NotNull(request);
        Check.NotNull(context);
        OrchestrationState state = await this.client.GetOrchestrationStateAsync(request.InstanceId, executionId: null);
        if (state == null)
        {
            return new P.GetInstanceResponse() { Exists = false };
        }

        return CreateGetInstanceResponse(state, request);
    }

    /// <inheritdoc/>
    public override async Task<P.QueryInstancesResponse> QueryInstances(
        P.QueryInstancesRequest request, ServerCallContext context)
    {
        Check.NotNull(request);
        Check.NotNull(context);
        if (this.client is IOrchestrationServiceQueryClient queryClient)
        {
            OrchestrationQuery query = ProtobufUtils.ToOrchestrationQuery(request);
            OrchestrationQueryResult result = await queryClient.GetOrchestrationWithQueryAsync(
                query, context.CancellationToken);
            return result.CreateQueryInstancesResponse();
        }
        else
        {
            throw new NotSupportedException($"{this.client.GetType().Name} doesn't support query operations.");
        }
    }

    /// <inheritdoc/>
    public override async Task<P.PurgeInstancesResponse> PurgeInstances(
        P.PurgeInstancesRequest request, ServerCallContext context)
    {
        Check.NotNull(request);
        Check.NotNull(context);

        if (this.client is IOrchestrationServicePurgeClient purgeClient)
        {
            PurgeResult result;
            switch (request.RequestCase)
            {
                case P.PurgeInstancesRequest.RequestOneofCase.InstanceId:
                    result = await purgeClient.PurgeInstanceStateAsync(request.InstanceId);
                    break;

                case P.PurgeInstancesRequest.RequestOneofCase.PurgeInstanceFilter:
                    PurgeInstanceFilter purgeInstanceFilter = ProtobufUtils.ToPurgeInstanceFilter(request);
                    result = await purgeClient.PurgeInstanceStateAsync(purgeInstanceFilter);
                    break;

                default:
                    throw new ArgumentException($"Unknown purge request type '{request.RequestCase}'.");
            }

            return result.ToPurgeInstancesResponse();
        }
        else
        {
            throw new NotSupportedException($"{this.client.GetType().Name} doesn't support purge operations.");
        }
    }

    /// <inheritdoc/>
    public override async Task<P.RaiseEventResponse> RaiseEvent(P.RaiseEventRequest request, ServerCallContext context)
    {
        Check.NotNull(request);
        Check.NotNull(context);
        await this.client.SendTaskOrchestrationMessageAsync(
            new TaskMessage
            {
                Event = new EventRaisedEvent(-1, request.Input)
                {
                    Name = request.Name,
                },
                OrchestrationInstance = new OrchestrationInstance
                {
                    InstanceId = request.InstanceId,
                },
            });

        // No fields in the response
        return new P.RaiseEventResponse();
    }

    /// <inheritdoc/>
    public override async Task<P.TerminateResponse> TerminateInstance(
        P.TerminateRequest request, ServerCallContext context)
    {
        Check.NotNull(request);
        Check.NotNull(context);
        await this.client.ForceTerminateTaskOrchestrationAsync(
            request.InstanceId,
            request.Output);

        // No fields in the response
        return new P.TerminateResponse();
    }

    static P.GetInstanceResponse CreateGetInstanceResponse(OrchestrationState state, P.GetInstanceRequest request)
    {
        return new P.GetInstanceResponse
        {
            Exists = true,
            OrchestrationState = new P.OrchestrationState
            {
                InstanceId = state.OrchestrationInstance.InstanceId,
                Name = state.Name,
                OrchestrationStatus = (P.OrchestrationStatus)state.OrchestrationStatus,
                CreatedTimestamp = Timestamp.FromDateTime(state.CreatedTime),
                LastUpdatedTimestamp = Timestamp.FromDateTime(state.LastUpdatedTime),
                Input = request.GetInputsAndOutputs ? state.Input : null,
                Output = request.GetInputsAndOutputs ? state.Output : null,
                CustomStatus = request.GetInputsAndOutputs ? state.Status : null,
                FailureDetails = request.GetInputsAndOutputs ? state.FailureDetails.GetFailureDetails() : null,
            },
        };
    }
}
