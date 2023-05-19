// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using P = Microsoft.DurableTask.Protobuf.Experimental;

namespace Microsoft.DurableTask.Worker.Grpc.Stream;

/// <summary>
/// Extensions for protobuf / gRPC types.
/// </summary>
static class ProtobufExtensions
{
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

    /// <summary>
    /// Converts a <see cref="OrchestrationMessage"/> to a <see cref="P.OrchestratorMessage"/>.
    /// </summary>
    /// <param name="message">The message to convert.</param>
    /// <returns>The converted message.</returns>
    public static P.OrchestratorMessage ToGrpcMessage(this OrchestrationMessage message)
    {
        Check.NotNull(message);
        P.OrchestratorMessage result = new() { Id = message.Id, Timestamp = message.Timestamp.ToTimestamp() };
        switch (message)
        {
            case EventReceived x:
                result.EventRaised = new()
                {
                    Input = x.Input,
                    Name = x.Name,
                };
                break;
        }

        return result;
    }

    /// <summary>
    /// Converts a <see cref="OrchestrationMessage"/> to a <see cref="P.OrchestratorAction"/>.
    /// </summary>
    /// <param name="message">The message to convert.</param>
    /// <returns>The converted action.</returns>
    public static P.OrchestratorAction ToGrpcAction(this OrchestrationMessage message)
    {
        Check.NotNull(message);
        P.OrchestratorAction action = new() { Id = message.Id };
        switch (message)
        {
            case SubOrchestrationScheduled x:
                action.OrchestrationScheduled = new()
                {
                    Name = x.Name.ToGrpcName(),
                    Input = x.Input,
                };

                if (x.Options is { } o)
                {
                    action.OrchestrationScheduled.Options = new()
                    {
                        InstanceId = o.InstanceId,
                        FireAndForget = o.FireAndForget,
                        InheritMetadata = o.InheritMetadata,
                    };

                    action.OrchestrationScheduled.Options.Metadata.AddAll(o.Metadata);
                }

                break;
            case TaskActivityScheduled x:
                action.TaskScheduled = new()
                {
                    Name = x.Name.ToGrpcName(),
                    Input = x.Input,
                };
                break;
            case ContinueAsNew x:
                action.Continued = new()
                {
                    Input = x.Result,
                    Version = x.Version,
                };

                foreach (OrchestrationMessage carryOver in x.CarryOverMessages)
                {
                    action.Continued.CarryOverMessages.Add(carryOver.ToGrpcMessage());
                }

                break;
            case ExecutionTerminated x:
                action.Terminated = new()
                {
                    Reason = x.Result,
                };
                break;
            case ExecutionCompleted x:
                action.Completed = new()
                {
                    Result = x.Result,
                    Error = x.Failure?.ToGrpcError(),
                };
                break;
            case TimerScheduled x:
                action.TimerCreated = new()
                {
                    FireAt = x.FireAt.ToTimestamp(),
                };
                break;
            case EventSent x:
                action.EventSent = new()
                {
                    InstanceId = x.InstanceId,
                    Name = x.Name,
                    Input = x.Input,
                };
                break;
        }

        return action;
    }
}
