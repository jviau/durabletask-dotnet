// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.History;
using Google.Protobuf.WellKnownTypes;
using P = Microsoft.DurableTask.Protobuf.Experimental;

namespace Microsoft.DurableTask.Grpc.Hub;

/// <summary>
/// Extensions for protobuf.
/// </summary>
static class ProtoExtensions
{
    static readonly P.TaskError EmptyError = new()
    {
        ErrorMessage = "Task Failure",
    };

    /// <summary>
    /// Converts a <see cref="DateTime"/> to a <see cref="Timestamp"/>.
    /// </summary>
    /// <param name="timestamp">The timestamp to convert.</param>
    /// <returns>The converted timestamp.</returns>
    public static Timestamp ToTimestamp(this DateTime timestamp) => Timestamp.FromDateTime(timestamp);

    /// <summary>
    /// Converts a <see cref="DateTimeOffset"/> to a <see cref="Timestamp"/>.
    /// </summary>
    /// <param name="timestamp">The timestamp to convert.</param>
    /// <returns>The converted timestamp.</returns>
    public static Timestamp ToTimestamp(this DateTimeOffset timestamp) => Timestamp.FromDateTimeOffset(timestamp);

    /// <summary>
    /// Checks if an orchestration state is terminal or not.
    /// </summary>
    /// <param name="state">The state to check.</param>
    /// <returns><c>true</c> for terminal state, <c>false</c> otherwise.</returns>
    public static bool IsTerminal(this P.OrchestrationState state)
    {
        return state is P.OrchestrationState.Canceled or P.OrchestrationState.Completed
            or P.OrchestrationState.Terminated or P.OrchestrationState.Failed;
    }

    /// <summary>
    /// Checks if an orchestration state is terminal or not.
    /// </summary>
    /// <param name="state">The state to check.</param>
    /// <returns><c>true</c> for terminal state, <c>false</c> otherwise.</returns>
    public static bool IsTerminal(this OrchestrationStatus state)
    {
        return state is OrchestrationStatus.Canceled or OrchestrationStatus.Completed
            or OrchestrationStatus.Terminated or OrchestrationStatus.Failed;
    }

    /// <summary>
    /// Converts a <see cref="P.OrchestrationState"/> to a <see cref="OrchestrationStatus"/>.
    /// </summary>
    /// <param name="state">The state to convert.</param>
    /// <returns>The converted state.</returns>
    public static OrchestrationStatus Convert(this P.OrchestrationState state)
    {
        return state switch
        {
            P.OrchestrationState.Pending => OrchestrationStatus.Pending,
            P.OrchestrationState.Running => OrchestrationStatus.Running,
            P.OrchestrationState.Suspended => OrchestrationStatus.Suspended,
            P.OrchestrationState.Completed => OrchestrationStatus.Completed,
            P.OrchestrationState.Failed => OrchestrationStatus.Failed,
            P.OrchestrationState.Terminated => OrchestrationStatus.Terminated,
            P.OrchestrationState.Canceled => OrchestrationStatus.Canceled,
            _ => (OrchestrationStatus)state,
        };
    }

    /// <summary>
    /// Converts a <see cref="HistoryEvent"/> to a <see cref="P.OrchestratorMessage"/>.
    /// </summary>
    /// <param name="history">The history event to convert.</param>
    /// <returns>The converted protobuf orchestrator message.</returns>
    public static P.OrchestratorMessage? ToMessage(this HistoryEvent history)
    {
        Check.NotNull(history);

        P.OrchestratorMessage message = new()
        {
            Id = history.EventId,
            Timestamp = history.Timestamp.ToTimestamp(),
        };

        switch (history)
        {
            case ExecutionStartedEvent e:
                message.Started = new()
                {
                    Input = e.Input,
                };
                break;
            case ContinueAsNewEvent e: // must be before ExecutionCompletedEvent.
                message.Continued = new()
                {
                    Input = e.Result,
                };
                break;
            case ExecutionTerminatedEvent e:
                message.Terminated = new()
                {
                    Reason = e.Input,
                };
                break;
            case ExecutionCompletedEvent e:
                message.Completed = new()
                {
                    Result = e.Result,
                    Error = e.FailureDetails?.ToError(),
                };
                break;
            case ExecutionResumedEvent:
                break; // P.ExecutionResumedEvent is NOT the same.
            case TaskScheduledEvent e:
                message.TaskScheduled = new()
                {
                    Name = new() { Name = e.Name, Version = e.Version },
                    Input = e.Input,
                };
                break;
            case TaskCompletedEvent e:
                message.TaskCompleted = new()
                {
                    ScheduledId = e.TaskScheduledId,
                    Result = e.Result,
                };
                break;
            case TaskFailedEvent e:
                message.TaskCompleted = new()
                {
                    ScheduledId = e.TaskScheduledId,
                    Error = e.FailureDetails?.ToError() ?? EmptyError,
                };
                break;
            case SubOrchestrationInstanceCreatedEvent e:
                message.OrchestrationScheduled = new()
                {
                    Name = new() { Name = e.Name, Version = e.Version },
                    Input = e.Input,
                    Options = new()
                    {
                        InstanceId = e.InstanceId,
                    },
                };
                break;
            case SubOrchestrationInstanceCompletedEvent e:
                message.OrchestrationCompleted = new()
                {
                    ScheduledId = e.TaskScheduledId,
                    Result = e.Result,
                };
                break;
            case SubOrchestrationInstanceFailedEvent e:
                message.OrchestrationCompleted = new()
                {
                    ScheduledId = e.TaskScheduledId,
                    Error = e.FailureDetails?.ToError() ?? EmptyError,
                };
                break;
            case TimerCreatedEvent e:
                message.TimerCreated = new()
                {
                    FireAt = e.FireAt.ToTimestamp(),
                };
                break;
            case TimerFiredEvent e:
                message.TimerFired = new()
                {
                    ScheduledId = e.TimerId,
                };
                break;
            case GenericEvent e:
                message.Generic = new()
                {
                    Name = e.EventType.ToString(),
                    Data = e.Data,
                };
                break;
            default:
                return null;
        }

        return message;
    }

    /// <summary>
    /// Converts a <see cref="FailureDetails"/> to a <see cref="P.TaskError"/>.
    /// </summary>
    /// <param name="details">The failure details to convert.</param>
    /// <returns>The converted protobuf task error.</returns>
    public static P.TaskError ToError(this FailureDetails details)
    {
        Check.NotNull(details);
        return new()
        {
            ErrorMessage = details.ErrorMessage,
            ErrorType = details.ErrorType,
            InnerError = details.InnerFailure?.ToError(),
            StackTrace = details.StackTrace,
        };
    }

    /// <summary>
    /// Converts a <see cref="P.OrchestratorAction"/> to a <see cref="HistoryEvent"/>.
    /// </summary>
    /// <param name="action">The action to convert.</param>
    /// <returns>The converted history event.</returns>
    public static HistoryEvent ToHistoryEvent(this P.OrchestratorAction action)
    {
        Check.NotNull(action);
        return action switch
        {
            { Completed: { } a } => new ExecutionCompletedEvent(
                action.Id, a.Result, a.GetStatus(), a.Error?.ToFailure()),
            { Terminated: { } a } => new ExecutionTerminatedEvent(action.Id, a.Reason),
            { Continued: { } a } => new ContinueAsNewEvent(action.Id, a.Input),
            { TaskScheduled: { } a } => new TaskScheduledEvent(
                action.Id, a.Name.Name, a.Name.Version, a.Input),
            { OrchestrationScheduled: { } a } => new SubOrchestrationInstanceCreatedEvent(action.Id)
            {
                Name = a.Name.Name,
                Version = a.Name.Version,
                Input = a.Input,
                InstanceId = a.Options?.InstanceId ?? Guid.NewGuid().ToString("N"),
            },
            { TimerCreated: { } a } => new TimerCreatedEvent(action.Id, a.FireAt.ToDateTime()),
            { Generic: { } a } => new GenericEvent(action.Id, a.Data), // TODO: fit name in here?
        };
    }

    /// <summary>
    /// Gets the terminal status from a completed event.
    /// </summary>
    /// <param name="e">The completd event.</param>
    /// <returns>The terminal status: Completed or Failed.</returns>
    public static OrchestrationStatus GetStatus(this P.ExecutionCompletedEvent e)
    {
        Check.NotNull(e);
        return e switch
        {
            { Error: not null } => OrchestrationStatus.Failed,
            _ => OrchestrationStatus.Completed,
        };
    }

    /// <summary>
    /// Convert a <see cref="P.TaskError"/> to a <see cref="FailureDetails"/>.
    /// </summary>
    /// <param name="error">The task error to convert.</param>
    /// <returns>The converted failure details.</returns>
    public static FailureDetails ToFailure(this P.TaskError error)
    {
        Check.NotNull(error);
        return new(
            error.ErrorType,
            error.ErrorMessage,
            error.StackTrace,
            error.InnerError?.ToFailure(),
            false);
    }

    /// <summary>
    /// Gets the tags for an new sub-orchestration from the request and current orchestrations tags.
    /// </summary>
    /// <param name="request">The request to get the tags from.</param>
    /// <param name="currentTags">The current tags to also use - optional.</param>
    /// <returns>A dictionary of tags, or <c>null</c> if no tags found.</returns>
    public static IDictionary<string, string>? GetTags(
        this P.SubOrchestrationScheduledEvent request, IDictionary<string, string>? currentTags = null)
    {
        Check.NotNull(request);

        return request.Options switch
        {
            { InheritMetadata: false, Metadata: null } => null,
            { InheritMetadata: false, Metadata: { } m } => m.Clone(),
            { InheritMetadata: true, Metadata: null } => currentTags,
            { InheritMetadata: true, Metadata: { } m } => currentTags?.MergeLeft(m) ?? m,
            _ => null,
        };
    }

    /// <summary>
    /// Converts a <see cref="OrchestrationState"/> to a <see cref="P.OrchestrationInfoResponse"/>.
    /// </summary>
    /// <param name="state">The state to convert.</param>
    /// <param name="expand">What values to include in the response.</param>
    /// <returns>The converted response.</returns>
    public static P.OrchestrationInfoResponse ToResponse(
        this OrchestrationState state, OrchestrationExpandDetail expand = OrchestrationExpandDetail.None)
    {
        P.OrchestrationInfoResponse response = new()
        {
            Id = new()
            {
                InstanceId = state.OrchestrationInstance.InstanceId,
                ExecutionId = state.OrchestrationInstance.ExecutionId,
            },
            Name = new() { Name = state.Name, Version = state.Version },
            Status = new()
            {
                Status = state.OrchestrationStatus.ToProto(),
                SubStatus = state.Status,
            },
            CreatedAt = state.CreatedTime.ToTimestamp(),
            ScheduledStartAt = state.ScheduledStartTime?.ToTimestamp(),
            LastUpdatedAt = state.LastUpdatedTime.ToTimestamp(),
            Input = expand.HasFlag(OrchestrationExpandDetail.Input) ? state.Input : null,
            Output = expand.HasFlag(OrchestrationExpandDetail.Output) ? state.Output : null,
            Error = expand.HasFlag(OrchestrationExpandDetail.Output) ? state.FailureDetails?.ToError() : null,
        };

        if (expand.HasFlag(OrchestrationExpandDetail.Metadata))
        {
            response.Metadata.AddAll(state.Tags);
        }

        return response;
    }

    /// <summary>
    /// Converts a <see cref="OrchestrationStatus"/> to a <see cref="P.OrchestrationState"/>.
    /// </summary>
    /// <param name="status">The status to convert.</param>
    /// <returns>The converted state.</returns>
    public static P.OrchestrationState ToProto(this OrchestrationStatus status)
    {
        return status switch
        {
            OrchestrationStatus.Pending => P.OrchestrationState.Pending,
            OrchestrationStatus.Running => P.OrchestrationState.Running,
            OrchestrationStatus.Suspended => P.OrchestrationState.Suspended,
            OrchestrationStatus.Completed => P.OrchestrationState.Completed,
            OrchestrationStatus.Failed => P.OrchestrationState.Failed,
            OrchestrationStatus.Terminated => P.OrchestrationState.Terminated,
            OrchestrationStatus.Canceled => P.OrchestrationState.Canceled,
            _ => P.OrchestrationState.Unspecified,
        };
    }
}
