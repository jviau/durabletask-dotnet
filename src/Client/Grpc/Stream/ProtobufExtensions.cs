// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using P = Microsoft.DurableTask.Protobuf.Experimental;

namespace Microsoft.DurableTask.Client.Grpc.Stream;

/// <summary>
/// Extensions for protobuf / gRPC types.
/// </summary>
static class ProtobufExtensions
{
    /// <summary>
    /// Converts a <see cref="P.OrchestrationState"/> to a <see cref="OrchestrationRuntimeStatus"/>.
    /// </summary>
    /// <param name="state">The state to convert.</param>
    /// <returns>The converted state.</returns>
    public static OrchestrationRuntimeStatus Convert(this P.OrchestrationState state)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        return state switch
        {
            P.OrchestrationState.Pending => OrchestrationRuntimeStatus.Pending,
            P.OrchestrationState.Running => OrchestrationRuntimeStatus.Running,
            P.OrchestrationState.Suspended => OrchestrationRuntimeStatus.Suspended,
            P.OrchestrationState.Completed => OrchestrationRuntimeStatus.Completed,
            P.OrchestrationState.Failed => OrchestrationRuntimeStatus.Failed,
            P.OrchestrationState.Terminated => OrchestrationRuntimeStatus.Terminated,
            P.OrchestrationState.Canceled => OrchestrationRuntimeStatus.Canceled,
            _ => (OrchestrationRuntimeStatus)state,
        };
#pragma warning restore CS0618 // Type or member is obsolete
    }

    /// <summary>
    /// Convert an <see cref="P.TaskError"/> to an <see cref="TaskFailureDetails"/>.
    /// </summary>
    /// <param name="error">The task error to convert.</param>
    /// <returns>The converted failure details.</returns>
    public static TaskFailureDetails Convert(this P.TaskError error)
    {
        Check.NotNull(error);
        return new(error.ErrorType, error.ErrorMessage, error.StackTrace, error.InnerError?.Convert());
    }

    /// <summary>
    /// Convert an <see cref="TaskFailureDetails"/> to an <see cref="P.TaskError"/>.
    /// </summary>
    /// <param name="details">The failure details to convert.</param>
    /// <returns>The converted error.</returns>
    public static P.TaskError ToGrpcError(this TaskFailureDetails details)
    {
        Check.NotNull(details);
        return new()
        {
            ErrorType = details.ErrorType,
            ErrorMessage = details.ErrorMessage,
            StackTrace = details.StackTrace,
            InnerError = details.InnerFailure?.ToGrpcError(),
        };
    }

    /// <summary>
    /// Convert an <see cref="Exception"/> to an <see cref="P.TaskError"/>.
    /// </summary>
    /// <param name="ex">The exception to convert.</param>
    /// <returns>The converted error.</returns>
    public static P.TaskError ToGrpcError(this Exception ex)
    {
        Check.NotNull(ex);
        return new()
        {
            ErrorType = ex.GetType().FullName,
            ErrorMessage = ex.Message,
            StackTrace = ex.StackTrace,
            InnerError = ex.InnerException?.ToGrpcError(),
        };
    }

    /// <summary>
    /// Convert a <see cref="P.TaskName"/> to a <see cref="TaskName"/>.
    /// </summary>
    /// <param name="name">The name to convert.</param>
    /// <returns>The converted name.</returns>
    public static TaskName Convert(this P.TaskName name)
    {
        Check.NotNull(name);
        return new(name.Name);
    }

    /// <summary>
    /// Convert a <see cref="TaskName"/> to a <see cref="P.TaskName"/>.
    /// </summary>
    /// <param name="name">The name to convert.</param>
    /// <returns>The converted name.</returns>
    public static P.TaskName ToGrpcName(this TaskName name)
    {
        return new()
        {
            Name = name.Name,
            Version = name.Version,
        };
    }
}
