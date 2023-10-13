// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using DurableTask.Core;
using DurableTask.Core.History;

namespace Microsoft.DurableTask.Worker.OrchestrationServiceShim;

/// <summary>
/// An orchestration channel which shims over <see cref="TaskOrchestrationWorkItem"/>.
/// </summary>
partial class ShimOrchestrationChannel : Channel<OrchestrationMessage>
{
    readonly IOrchestrationService service;
    readonly TaskOrchestrationWorkItem workItem;

    List<OrchestrationMessage>? pendingMessages;
    List<TaskMessage>? activityMessages;
    List<TaskMessage>? orchestratorMessages;
    List<TaskMessage>? timerMessages;

    bool abort;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShimOrchestrationChannel"/> class.
    /// </summary>
    /// <param name="service">The orchestration service.</param>
    /// <param name="workItem">The orchestration work item.</param>
    public ShimOrchestrationChannel(IOrchestrationService service, TaskOrchestrationWorkItem workItem)
    {
        this.service = Check.NotNull(service);
        this.workItem = Check.NotNull(workItem);
        this.Reader = new ShimReader(this);
        this.Writer = new ShimWriter(this);
    }

    /// <summary>
    /// Gets a value indicating whether this channel is replaying or not.
    /// </summary>
    public bool IsReplaying => ((ShimReader)this.Reader).IsReplaying;

    /// <summary>
    /// Gets a value indicating whether this orchestration was continued as new.
    /// </summary>
    public bool ContinueAsNew { get; private set; }

    /// <summary>
    /// Gets the list of activity messages.
    /// </summary>
    List<TaskMessage> ActivityMessages => this.activityMessages ??= new();

    /// <summary>
    /// Gets the list of orchestrator messages.
    /// </summary>
    List<TaskMessage> OrchestratorMessages => this.orchestratorMessages ??= new();

    /// <summary>
    /// Gets the list of orchestrator messages.
    /// </summary>
    List<TaskMessage> TimerMessages => this.timerMessages ??= new();

    OrchestrationRuntimeState State
    {
        get => this.workItem.OrchestrationRuntimeState;
        set => this.workItem.OrchestrationRuntimeState = value;
    }

    /// <summary>
    /// Completes the current execution, processing all events.
    /// </summary>
    /// <param name="isSession">True if this is a session iteration, false otherwise.</param>
    /// <returns>A task that completes when this orchestration execution is committed.</returns>
    public async Task CompleteExecutionAsync(bool isSession = false)
    {
        if (this.abort)
        {
            await this.service.AbandonTaskOrchestrationWorkItemAsync(this.workItem);
        }

        bool needComplete = false;
        if (this.pendingMessages is not null)
        {
            foreach (OrchestrationMessage message in this.pendingMessages)
            {
                needComplete = true;
                this.ProcessMessage(message);
            }
        }

        if (!isSession)
        {
            needComplete = true;
            this.State.AddEvent(new OrchestratorCompletedEvent(-1));
        }

        this.pendingMessages = null;
        if (needComplete)
        {
            // We have at least 1 new event or message to send.
            await this.service.CompleteTaskOrchestrationWorkItemAsync(
                this.workItem,
                this.State,
                this.ActivityMessages,
                this.OrchestratorMessages,
                this.TimerMessages,
                continuedAsNewMessage: null,
                this.State.GetState());

            this.activityMessages = null;
            this.orchestratorMessages = null;
            this.timerMessages = null;
        }
    }

    static OrchestrationMessage ToMessage(HistoryEvent historyEvent)
    {
        return historyEvent switch
        {
            OrchestratorCompletedEvent => null!,
            OrchestratorStartedEvent e => new OrchestratorStarted(e.Timestamp),
            ExecutionStartedEvent e => new ExecutionStarted(e.Timestamp, e.Input),
            ExecutionTerminatedEvent e => new ExecutionTerminated(-1, e.Timestamp, e.Input),
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
                    OrchestrationInstance = this.State.OrchestrationInstance,
                });
                break;
            case TimerScheduled m:
                DateTime fireAt = m.FireAt.UtcDateTime;
                history = new TimerCreatedEvent(m.Id, fireAt);
                this.TimerMessages.Add(new TaskMessage
                {
                    Event = new TimerFiredEvent(-1, fireAt) { TimerId = m.Id },
                    OrchestrationInstance = this.State.OrchestrationInstance,
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
                        OrchestrationInstance = this.State.OrchestrationInstance,
                        Name = this.State.Name,
                        Version = this.State.Version,
                        TaskScheduleId = scheduled.EventId,
                    },
                    Tags = m.Options?.BuildMetadata(this.State.Tags),
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
                OrchestrationRuntimeState newState = new() { Status = this.State.Status, };
                newState.AddEvent(new OrchestratorStartedEvent(01));
                newState.AddEvent(new ExecutionStartedEvent(-1, m.Result)
                {
                    OrchestrationInstance = new()
                    {
                        InstanceId = this.State.OrchestrationInstance!.InstanceId,
                        ExecutionId = Guid.NewGuid().ToString(),
                    },
                    Tags = this.State.Tags,
                    ParentInstance = this.State.ParentInstance,
                    Name = this.State.Name,
                    Version = m.Version ?? this.State.Version,
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

                this.State = newState;
                this.ContinueAsNew = true;
                return;
            case ExecutionTerminated m:
                // TODO: fill out FailureDetails?
                history = new ExecutionCompletedEvent(m.Id, m.Result, OrchestrationStatus.Terminated);
                if (this.State.ParentInstance is { } p1)
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
                if (this.State.ParentInstance is { } p2)
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

        this.State.AddEvent(history);
    }

    async Task<bool> TryRenewSessionAsync(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (this.abort)
        {
            return false;
        }

        if (this.workItem.Session is { } session)
        {
            await this.CompleteExecutionAsync(isSession: true);
            IList<TaskMessage> messages = await session.FetchNewOrchestrationMessagesAsync(this.workItem);
            if (messages is { Count: > 0 })
            {
                this.State.NewEvents.Clear();
                this.workItem.NewMessages = messages;
                this.workItem.PrepareForRun(isSession: true);
                return true;
            }
        }

        return false;
    }
}
