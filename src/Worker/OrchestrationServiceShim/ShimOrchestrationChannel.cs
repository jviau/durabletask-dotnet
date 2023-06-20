// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using DurableTask.Core;
using DurableTask.Core.History;

namespace Microsoft.DurableTask.Worker.OrchestrationServiceShim;

/// <summary>
/// An orchestration channel which shims over <see cref="OrchestrationRuntimeState"/>.
/// </summary>
partial class ShimOrchestrationChannel : Channel<OrchestrationMessage>
{
    OrchestrationRuntimeState state;
    List<OrchestrationMessage>? pendingMessages;
    List<TaskMessage>? activityMessages;
    List<TaskMessage>? orchestratorMessages;
    List<TaskMessage>? timerMessages;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShimOrchestrationChannel"/> class.
    /// </summary>
    /// <param name="runtimeState">The orchestration runtime state.</param>
    public ShimOrchestrationChannel(OrchestrationRuntimeState runtimeState)
    {
        this.state = Check.NotNull(runtimeState);
        this.Reader = new ShimReader(this);
        this.Writer = new ShimWriter(this);
    }

    /// <summary>
    /// Gets the list of activity messages.
    /// </summary>
    public List<TaskMessage> ActivityMessages => this.activityMessages ??= new();

    /// <summary>
    /// Gets the list of orchestrator messages.
    /// </summary>
    public List<TaskMessage> OrchestratorMessages => this.orchestratorMessages ??= new();

    /// <summary>
    /// Gets the list of orchestrator messages.
    /// </summary>
    public List<TaskMessage> TimerMessages => this.timerMessages ??= new();

    /// <summary>
    /// Gets a value indicating whether this channel is replaying or not.
    /// </summary>
    public bool IsReplaying => ((ShimReader)this.Reader).IsReplaying;

    /// <summary>
    /// Gets a value indicating whether to abort this orchestration.
    /// </summary>
    public bool Abort { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this orchestration was continued as new.
    /// </summary>
    public bool ContinueAsNew { get; private set; }

    /// <summary>
    /// Completes the current execution, processing all events.
    /// </summary>
    /// <returns>The <see cref="OrchestrationRuntimeState"/> with new events added.</returns>
    public OrchestrationRuntimeState CompleteExecution()
    {
        if (this.pendingMessages is not null)
        {
            foreach (OrchestrationMessage message in this.pendingMessages)
            {
                this.ProcessMessage(message);
            }

            this.state.AddEvent(new OrchestratorCompletedEvent(-1));
            this.pendingMessages.Clear();
        }

        return this.state;
    }

    static OrchestrationMessage ToMessage(HistoryEvent historyEvent)
    {
        return historyEvent switch
        {
            OrchestratorCompletedEvent => null!,
            OrchestratorStartedEvent e => new OrchestratorStarted(e.Timestamp),
            ExecutionStartedEvent e => new ExecutionStarted(e.Timestamp, e.Input),
            ExecutionTerminatedEvent e => new ExecutionTerminated(e.Timestamp, e.Input),
            ContinueAsNewEvent e => new ContinueAsNew(e.EventId, e.Timestamp, e.Result),
            ExecutionCompletedEvent e => new ExecutionCompleted(
                e.EventId, e.Timestamp, e.Result, e.FailureDetails?.ConvertFromCore()),
            TaskScheduledEvent e => new TaskActivityScheduled(e.EventId, e.Timestamp, e.GetName(), e.Input),
            TaskCompletedEvent e => new TaskActivityCompleted(
                e.EventId, e.Timestamp, e.TaskScheduledId, e.Result, null),
            TaskFailedEvent e => new TaskActivityCompleted(
                e.EventId, e.Timestamp, e.TaskScheduledId, null, e.FailureDetails?.ConvertFromCore()),
            SubOrchestrationInstanceCreatedEvent e => new SubOrchestrationScheduled(
                e.EventId, e.Timestamp, e.GetName(), e.Input, e.GetOptions()),
            SubOrchestrationInstanceCompletedEvent e => new SubOrchestrationCompleted(
                e.EventId, e.Timestamp, e.TaskScheduledId, e.Result, null),
            SubOrchestrationInstanceFailedEvent e => new SubOrchestrationCompleted(
                e.EventId, e.Timestamp, e.TaskScheduledId, null, e.FailureDetails?.ConvertFromCore()),
            TimerCreatedEvent e => new TimerScheduled(e.EventId, e.Timestamp, e.FireAt),
            TimerFiredEvent e => new TimerFired(e.EventId, e.Timestamp, e.TimerId),
            EventRaisedEvent e => new EventReceived(e.EventId, e.Timestamp, e.Name, e.Input),
            EventSentEvent e => new EventSent(e.EventId, e.Timestamp, e.InstanceId, e.Name, e.Input),
            _ => throw new NotSupportedException(),
        };
    }

    void EnqueueMessage(OrchestrationMessage message)
    {
        Check.NotNull(message);
        this.pendingMessages ??= new();
        this.pendingMessages.Add(message);
    }

    void ProcessMessage(OrchestrationMessage message)
    {
        if (this.ContinueAsNew)
        {
            // We already continued as new. Ignore any other messages.
            return;
        }

        HistoryEvent history;
        switch (message)
        {
            case TaskActivityScheduled m:
                history = new TaskScheduledEvent(m.Id)
                {
                    Name = m.Name.Name,
                    Version = m.Name.Version,
                    Input = m.Input,
                };

                this.ActivityMessages.Add(new TaskMessage
                {
                    Event = history,
                    OrchestrationInstance = this.state.OrchestrationInstance,
                });
                break;
            case TimerScheduled m:
                DateTime fireAt = m.FireAt.UtcDateTime;
                history = new TimerCreatedEvent(m.Id, fireAt);
                this.TimerMessages.Add(new TaskMessage
                {
                    Event = new TimerFiredEvent(-1, fireAt) { TimerId = m.Id },
                    OrchestrationInstance = this.state.OrchestrationInstance,
                });
                break;
            case SubOrchestrationScheduled m:
                SubOrchestrationInstanceCreatedEvent scheduled = new(m.Id)
                {
                    Name = m.Name.Name,
                    Version = m.Name.Version,
                    Input = m.Input,
                    InstanceId = m.Options?.InstanceId ?? Guid.NewGuid().ToString("N"),
                };

                history = scheduled;
                ExecutionStartedEvent startedEvent = new(-1, m.Input)
                {
                    Name = scheduled.Name,
                    Version = scheduled.Version,
                    OrchestrationInstance = new OrchestrationInstance
                    {
                        InstanceId = scheduled.InstanceId,
                        ExecutionId = Guid.NewGuid().ToString(),
                    },
                    ParentInstance = new ParentInstance
                    {
                        OrchestrationInstance = this.state.OrchestrationInstance,
                        Name = this.state.Name,
                        Version = this.state.Version,
                        TaskScheduleId = scheduled.EventId,
                    },
                    Tags = m.Options?.BuildMetadata(this.state.Tags),
                };

                this.OrchestratorMessages.Add(new TaskMessage
                {
                    Event = startedEvent,
                    OrchestrationInstance = startedEvent.OrchestrationInstance,
                });
                break;
            case EventSent m:
                history = new EventSentEvent(m.Id)
                {
                    InstanceId = m.InstanceId,
                    Name = m.Name,
                    Input = m.Input,
                };

                this.OrchestratorMessages.Add(new TaskMessage
                {
                    Event = history,
                    OrchestrationInstance = new OrchestrationInstance { InstanceId = m.InstanceId },
                });
                break;
            case ContinueAsNew m:
                OrchestrationRuntimeState newState = new() { Status = this.state.Status, };
                newState.AddEvent(new OrchestratorStartedEvent(01));
                newState.AddEvent(new ExecutionStartedEvent(-1, m.Result)
                {
                    OrchestrationInstance = new()
                    {
                        InstanceId = this.state.OrchestrationInstance!.InstanceId,
                        ExecutionId = Guid.NewGuid().ToString(),
                    },
                    Tags = this.state.Tags,
                    ParentInstance = this.state.ParentInstance,
                    Name = this.state.Name,
                    Version = m.Version ?? this.state.Version,
                });

                foreach (OrchestrationMessage c in m.CarryOverMessages)
                {
                    // TODO: support other carry over events?
                    if (c is not EventReceived e)
                    {
                        continue;
                    }

                    newState.AddEvent(new EventRaisedEvent(e.Id, e.Input) { Name = e.Name });
                }

                this.state = newState;
                this.ContinueAsNew = true;
                return;
            case ExecutionTerminated m:
                // TODO: fill out FailureDetails?
                history = new ExecutionCompletedEvent(m.Id, m.Result, OrchestrationStatus.Terminated);
                if (this.state.ParentInstance is { } p1)
                {
                    HistoryEvent completed = new SubOrchestrationInstanceFailedEvent(
                        -1, p1.TaskScheduleId, m.Result, null, null);
                    this.OrchestratorMessages.Add(new TaskMessage
                    {
                        Event = completed,
                        OrchestrationInstance = p1.OrchestrationInstance,
                    });
                }

                break;
            case ExecutionCompleted m:
                history = new ExecutionCompletedEvent(m.Id, m.Result, m.GetStatus());
                if (this.state.ParentInstance is { } p2)
                {
                    HistoryEvent completed = m switch
                    {
                        { Failure: { } f } => new SubOrchestrationInstanceFailedEvent(
                            -1, p2.TaskScheduleId, m.Result, null, f.ToCore()),
                        _ => new SubOrchestrationInstanceCompletedEvent(-1, p2.TaskScheduleId, m.Result),
                    };

                    this.OrchestratorMessages.Add(new TaskMessage
                    {
                        Event = completed,
                        OrchestrationInstance = p2.OrchestrationInstance,
                    });
                }

                break;
            default:
                throw new NotSupportedException();
        }

        this.state.AddEvent(history);
    }
}
