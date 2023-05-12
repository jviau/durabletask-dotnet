// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using static Microsoft.DurableTask.Protobuf.TaskHubSidecarService;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc;

/// <summary>
/// An activity work item from the gRPC sidecar.
/// </summary>
class GrpcActivityWorkItem : ActivityWorkItem
{
    readonly P.ActivityRequest request;
    readonly TaskHubSidecarServiceClient sidecar;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcActivityWorkItem"/> class.
    /// </summary>
    /// <param name="request">The gRPC activity request.</param>
    /// <param name="sidecar">The sidecar service.</param>
    public GrpcActivityWorkItem(P.ActivityRequest request, TaskHubSidecarServiceClient sidecar)
        : base(request.OrchestrationInstance.InstanceId, request.Name)
    {
        this.request = Check.NotNull(request);
        this.sidecar = Check.NotNull(sidecar);
    }

    /// <inheritdoc/>
    public override string? Input => this.request.Input;

    /// <inheritdoc/>
    public override async ValueTask CompleteAsync(string? result)
    {
        P.ActivityResponse response = new()
        {
            InstanceId = this.request.OrchestrationInstance.InstanceId,
            TaskId = this.request.TaskId,
            Result = result,
        };

        await this.sidecar.CompleteActivityTaskAsync(response);
    }

    /// <inheritdoc/>
    public override async ValueTask FailAsync(Exception exception)
    {
        P.ActivityResponse response = new()
        {
            InstanceId = this.request.OrchestrationInstance.InstanceId,
            TaskId = this.request.TaskId,
            FailureDetails = new()
            {
                ErrorType = exception.GetType().FullName,
                ErrorMessage = exception.Message,
                StackTrace = exception.StackTrace,
            },
        };

        await this.sidecar.CompleteActivityTaskAsync(response);
    }

    /// <inheritdoc/>
    public override ValueTask<bool> TryRenewLockAsync(CancellationToken cancellation = default) => new(true);
}
