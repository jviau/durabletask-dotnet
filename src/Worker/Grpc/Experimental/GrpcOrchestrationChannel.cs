// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using static Microsoft.DurableTask.Protobuf.TaskHubSidecarService;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc;

/// <summary>
/// A threading channel for a gRPC orchestrator request.
/// </summary>
partial class GrpcOrchestrationChannel : Channel<OrchestrationMessage>
{
    readonly P.OrchestratorRequest request;
    readonly TaskHubSidecarServiceClient sidecar;
    readonly P.OrchestratorResponse response;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcOrchestrationChannel"/> class.
    /// </summary>
    /// <param name="request">The gRPC orchestration request.</param>
    /// <param name="sidecar">The sidecar service.</param>
    public GrpcOrchestrationChannel(P.OrchestratorRequest request, TaskHubSidecarServiceClient sidecar)
    {
        this.request = Check.NotNull(request);
        this.sidecar = Check.NotNull(sidecar);
        this.Reader = new GrpcReader(this);
        this.Writer = new GrpcWriter(this);
        this.response = new()
        {
            InstanceId = request.InstanceId,
        };
    }

    /// <summary>
    /// Gets a value indicating whether this channel is replaying or not.
    /// </summary>
    public bool IsReplaying => ((GrpcReader)this.Reader).IsReplaying;

    /// <summary>
    /// Gets or sets the custom status.
    /// </summary>
    public string? CustomStatus
    {
        get => this.response.CustomStatus;
        set => this.response.CustomStatus = value;
    }

    bool Abort { get; set; }

    /// <summary>
    /// Flushes the response message to the gRPC side car.
    /// </summary>
    /// <returns>A task that represents the call to the gRPC side car.</returns>
    public async Task FlushAsync()
    {
        if (this.Abort)
        {
            return;
        }

        await this.sidecar.CompleteOrchestratorTaskAsync(this.response);
    }

    static OrchestrationMessage ToMessage(P.HistoryEvent e)
    {
        DateTimeOffset timestamp = e.Timestamp.ToDateTimeOffset();
        return e switch
        {
            { OrchestratorCompleted: not null } => null!, // not important, drop this
            { OrchestratorStarted: { } x } => new OrchestratorStarted(timestamp),
            { ExecutionStarted: { } x } => new ExecutionStarted(timestamp, x.Input),
            { ExecutionTerminated: { } x } => new ExecutionTerminated(timestamp, x.Input),
            { ContinueAsNew: { } x } => new ContinueAsNew(e.EventId, timestamp, x.Input),
            { ExecutionCompleted: { } x } => new ExecutionCompleted(
                e.EventId, timestamp, x.Result, x.FailureDetails?.ToTaskFailureDetails()),
            { TaskScheduled: { } x } => new TaskActivityScheduled(e.EventId, timestamp, x.Name, x.Input),
            { TaskCompleted: { } x } => new TaskActivityCompleted(
                e.EventId, timestamp, x.TaskScheduledId, x.Result, null),
            { TaskFailed: { } x } => new TaskActivityCompleted(
                e.EventId, timestamp, x.TaskScheduledId, null, x.FailureDetails.ToTaskFailureDetails()),
            { SubOrchestrationInstanceCreated: { } x } => new SubOrchestrationScheduled(
                e.EventId, timestamp, x.Name, x.Input, new(x.InstanceId)),
            { SubOrchestrationInstanceCompleted: { } x } => new SubOrchestrationCompleted(
                e.EventId, timestamp, x.TaskScheduledId, x.Result, null),
            { SubOrchestrationInstanceFailed: { } x } => new SubOrchestrationCompleted(
                e.EventId, timestamp, x.TaskScheduledId, null, x.FailureDetails.ToTaskFailureDetails()),
            { TimerCreated: { } x } => new TimerScheduled(
                e.EventId, timestamp, x.FireAt.ToDateTimeOffset()),
            { TimerFired: { } x } => new TimerFired(
                e.EventId, timestamp, x.TimerId),
            _ => throw new NotSupportedException(),
        };
    }

    static P.TaskFailureDetails? ToProtobuf(TaskFailureDetails? details)
    {
        if (details is null)
        {
            return null;
        }

        return new()
        {
            ErrorType = details.ErrorType ?? "(unknown)",
            ErrorMessage = details.ErrorMessage ?? "(unknown)",
            StackTrace = details.StackTrace,
            IsNonRetriable = true,
            InnerFailure = ToProtobuf(details.InnerFailure),
        };
    }

    static P.OrchestratorAction ToAction(OrchestrationMessage message)
    {
        P.OrchestratorAction action = new() { Id = message.Id, };
        switch (message)
        {
            case SubOrchestrationScheduled x:
                action.CreateSubOrchestration = new()
                {
                    Input = x.Input,
                    InstanceId = x.Options?.InstanceId ?? Guid.NewGuid().ToString("N"),
                    Name = x.Name.Name,
                    Version = x.Name.Version,
                };
                break;
            case TaskActivityScheduled x:
                action.ScheduleTask = new()
                {
                    Name = x.Name.Name,
                    Version = x.Name.Version,
                    Input = x.Input,
                };
                break;
            case ContinueAsNew x:
                action.CompleteOrchestration = new()
                {
                    Result = x.Result,
                    OrchestrationStatus = P.OrchestrationStatus.ContinuedAsNew,
                };

                foreach (OrchestrationMessage carryOver in x.CarryOverMessages)
                {
                    action.CompleteOrchestration.CarryoverEvents.Add(ToHistoryEvent(carryOver));
                }

                break;
            case ExecutionTerminated x:
                action.CompleteOrchestration = new()
                {
                    Result = x.Result,
                    OrchestrationStatus = P.OrchestrationStatus.Terminated,
                };
                break;
            case ExecutionCompleted x:
                action.CompleteOrchestration = new()
                {
                    Result = x.Result,
                    FailureDetails = ToProtobuf(x.Failure),
                    OrchestrationStatus = x.Failure is null
                        ? P.OrchestrationStatus.Completed : P.OrchestrationStatus.Failed,
                };
                break;
            case TimerScheduled x:
                action.CreateTimer = new()
                {
                    FireAt = x.FireAt.ToTimestamp(),
                };
                break;
        }

        return action;
    }

    static P.HistoryEvent ToHistoryEvent(OrchestrationMessage message)
    {
        P.HistoryEvent historyEvent = new() { EventId = message.Id, Timestamp = message.Timestamp.ToTimestamp() };
        switch (message)
        {
            case EventReceived x:
                historyEvent.EventRaised = new()
                {
                    Name = x.Name,
                    Input = x.Input,
                };
                break;
        }

        return historyEvent;
    }

    void EnqueueAction(OrchestrationMessage message)
    {
        this.response.Actions.Add(ToAction(message));
    }
}
