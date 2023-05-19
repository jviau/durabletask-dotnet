// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using static Microsoft.DurableTask.Protobuf.Experimental.DurableTaskHub;
using P = Microsoft.DurableTask.Protobuf.Experimental;

namespace Microsoft.DurableTask.Worker.Grpc.Stream;

/// <summary>
/// An activity work item from the gRPC sidecar.
/// </summary>
class GrpcActivityWorkItem : ActivityWorkItem
{
    readonly P.ActivityWorkItem request;
    readonly DurableTaskHubClient client;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcActivityWorkItem"/> class.
    /// </summary>
    /// <param name="request">The gRPC activity work item.</param>
    /// <param name="client">The TaskHub service client.</param>
    public GrpcActivityWorkItem(P.ActivityWorkItem request, DurableTaskHubClient client)
        : base(Check.NotNull(request).Parent.InstanceId, request.Name.Name)
    {
        this.request = Check.NotNull(request);
        this.client = Check.NotNull(client);
    }

    /// <inheritdoc/>
    public override string? Input => this.request.Input;

    /// <inheritdoc/>
    public override async ValueTask CompleteAsync(string? result)
    {
        P.ActivityResult response = new()
        {
            TaskId = this.request.Id,
            Parent = this.request.Parent,
            Result = result,
        };

        await this.client.CompleteTaskActivityAsync(response);
    }

    /// <inheritdoc/>
    public override async ValueTask FailAsync(Exception exception)
    {
        static P.TaskError ToError(Exception ex)
        {
            P.TaskError error = new()
            {
                ErrorType = ex.GetType().FullName,
                ErrorMessage = ex.Message,
                StackTrace = ex.StackTrace,
            };

            if (ex.InnerException is not null)
            {
                error.InnerError = ToError(ex.InnerException);
            }

            return error;
        }

        P.ActivityResult response = new()
        {
            TaskId = this.request.Id,
            Parent = this.request.Parent,
            Error = ToError(exception),
        };

        await this.client.CompleteTaskActivityAsync(response);
    }
}
