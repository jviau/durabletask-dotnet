// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections;
using System.Data;
using DurableTask.Core;
using DurableTask.Core.History;
using Google.Protobuf;
using P = Microsoft.DurableTask.Protobuf.Experimental;

namespace Microsoft.DurableTask.Grpc.Hub;

/// <summary>
/// Collection of actions for an orchestration execution.
/// </summary>
class OrchestratorActionCollection : ICollection<P.OrchestratorAction>
{
    readonly List<P.OrchestratorAction> actions = new();

    /// <summary>
    /// Gets the orchestrators sub-status.
    /// </summary>
    public string? SubStatus { get; private set; }

    /// <inheritdoc/>
    public int Count => this.actions.Count;

    /// <inheritdoc/>
    public bool IsReadOnly => ((ICollection<P.OrchestratorAction>)this.actions).IsReadOnly;

    /// <summary>
    /// Apply all the actions in this collection to a <see cref="OrchestrationRuntimeState"/>.
    /// </summary>
    /// <param name="state">The runtime state. Modified in most cases, or replaced with a new instance on ContinueAsNew.</param>
    /// <param name="activityMessages">The new activity messages.</param>
    /// <param name="orchestratorMessages">The new orchestrator messages.</param>
    /// <param name="timerMessages">The new timer messages.</param>
    /// <param name="updatedState">The updated orchestration state.</param>
    /// <param name="continueAsNew">True if continue as new, false otherwise.</param>
    public void ApplyActions(
        ref OrchestrationRuntimeState state,
        out IList<TaskMessage> activityMessages,
        out IList<TaskMessage> orchestratorMessages,
        out IList<TaskMessage> timerMessages,
        out OrchestrationState? updatedState,
        out bool continueAsNew)
    {
        Check.NotNull(state);
        if (state.OrchestrationInstance is null)
        {
            throw new ArgumentException("OrchestrationRuntimeState doesn't contain an instance ID.", nameof(state));
        }

        IList<TaskMessage>? newActivityMessages = null;
        IList<TaskMessage>? newTimerMessages = null;
        IList<TaskMessage>? newOrchestratorMessages = null;
        FailureDetails? failureDetails = null;
        continueAsNew = false;

        state.Status = this.SubStatus;

        foreach (P.OrchestratorAction action in this)
        {
            HistoryEvent history = action.ToHistoryEvent();
            switch (history)
            {
                case TaskScheduledEvent e:
                    newActivityMessages ??= new List<TaskMessage>();
                    newActivityMessages.Add(new TaskMessage
                    {
                        Event = e,
                        OrchestrationInstance = state.OrchestrationInstance,
                    });
                    break;
                case TimerCreatedEvent e:
                    newTimerMessages ??= new List<TaskMessage>();
                    newTimerMessages.Add(new TaskMessage
                    {
                        Event = new TimerFiredEvent(-1, e.FireAt)
                        {
                            TimerId = e.EventId,
                        },
                        OrchestrationInstance = state.OrchestrationInstance,
                    });
                    break;
                case SubOrchestrationInstanceCreatedEvent e:
                    ExecutionStartedEvent startedEvent = new(-1, e.Input)
                    {
                        Name = e.Name,
                        Version = e.Version,
                        OrchestrationInstance = new OrchestrationInstance
                        {
                            InstanceId = e.InstanceId,
                            ExecutionId = Guid.NewGuid().ToString(),
                        },
                        ParentInstance = new ParentInstance
                        {
                            OrchestrationInstance = state.OrchestrationInstance,
                            Name = state.Name,
                            Version = state.Version,
                            TaskScheduleId = e.EventId,
                        },
                        Tags = action.OrchestrationScheduled.GetTags(),
                    };

                    newOrchestratorMessages ??= new List<TaskMessage>();
                    newOrchestratorMessages.Add(new TaskMessage
                    {
                        Event = startedEvent,
                        OrchestrationInstance = startedEvent.OrchestrationInstance,
                    });

                    break;
                case EventRaisedEvent e:
                    newOrchestratorMessages ??= new List<TaskMessage>();
                    newOrchestratorMessages.Add(new TaskMessage
                    {
                        Event = e,
                        OrchestrationInstance = state.OrchestrationInstance,
                    });
                    break;
                case ContinueAsNewEvent e:
                    OrchestrationRuntimeState newState = new() { Status = state.Status };
                    newState.AddEvent(new OrchestratorStartedEvent(-1));
                    newState.AddEvent(new ExecutionStartedEvent(-1, e.Result)
                    {
                        OrchestrationInstance = new()
                        {
                            InstanceId = state.OrchestrationInstance.InstanceId,
                            ExecutionId = Guid.NewGuid().ToString(),
                        },
                        Tags = state.Tags,
                        ParentInstance = state.ParentInstance,
                        Name = state.Name,
                        Version = action.Continued?.Version ?? state.Version,
                    });

                    if (action.Continued is not null)
                    {
                        foreach (P.OrchestratorMessage? m in action.Continued.CarryOverMessages)
                        {
                            // TODO: support other carry over events?
                            if (m is not { EventRaised: { } raised })
                            {
                                continue;
                            }

                            newState.AddEvent(new EventRaisedEvent(m.Id, raised.Input) { Name = raised.Name });
                        }
                    }

                    state = newState;
                    activityMessages = Array.Empty<TaskMessage>();
                    orchestratorMessages = Array.Empty<TaskMessage>();
                    timerMessages = Array.Empty<TaskMessage>();
                    continueAsNew = true;
                    updatedState = null;
                    return;
                case ExecutionTerminatedEvent e:
                    // TODO: fill out FailureDetails?
                    history = new ExecutionCompletedEvent(e.EventId, e.Input, OrchestrationStatus.Terminated);
                    if (state.ParentInstance is not null)
                    {
                        ParentInstance p = state.ParentInstance;
                        HistoryEvent completed = new SubOrchestrationInstanceFailedEvent(
                                -1, p.TaskScheduleId, e.Input, null, null);

                        newOrchestratorMessages ??= new List<TaskMessage>();
                        newOrchestratorMessages.Add(new TaskMessage
                        {
                            Event = completed,
                            OrchestrationInstance = p.OrchestrationInstance,
                        });
                    }

                    break;
                case ExecutionCompletedEvent e:
                    // NOTE: Failure details aren't being stored in the orchestration history, currently.
                    history = new ExecutionCompletedEvent(e.EventId, e.Result, e.OrchestrationStatus);
                    failureDetails = e.FailureDetails;
                    if (state.ParentInstance is not null)
                    {
                        ParentInstance p = state.ParentInstance; // assigning via pattern matching 
                        HistoryEvent completed = e.OrchestrationStatus switch
                        {
                            OrchestrationStatus.Failed => new SubOrchestrationInstanceFailedEvent(
                                -1, p.TaskScheduleId, e.Result, null, e.FailureDetails),
                            _ => new SubOrchestrationInstanceCompletedEvent(-1, p.TaskScheduleId, e.Result),
                        };

                        newOrchestratorMessages ??= new List<TaskMessage>();
                        newOrchestratorMessages.Add(new TaskMessage
                        {
                            Event = completed,
                            OrchestrationInstance = p.OrchestrationInstance,
                        });
                    }

                    break;
            }

            state.AddEvent(history);
        }

        state.AddEvent(new OrchestratorCompletedEvent(-1));

        activityMessages = newActivityMessages ?? Array.Empty<TaskMessage>();
        timerMessages = newTimerMessages ?? Array.Empty<TaskMessage>();
        orchestratorMessages = newOrchestratorMessages ?? Array.Empty<TaskMessage>();

        updatedState = new OrchestrationState
        {
            OrchestrationInstance = state.OrchestrationInstance,
            ParentInstance = state.ParentInstance,
            Name = state.Name,
            Version = state.Version,
            Status = state.Status,
            Tags = state.Tags,
            OrchestrationStatus = state.OrchestrationStatus,
            CreatedTime = state.CreatedTime,
            CompletedTime = state.CompletedTime,
            LastUpdatedTime = DateTime.UtcNow,
            Size = state.Size,
            CompressedSize = state.CompressedSize,
            Input = state.Input,
            Output = state.Output,
            ScheduledStartTime = state.ExecutionStartedEvent?.ScheduledStartTime,
            FailureDetails = failureDetails,
        };
    }

    /// <summary>
    /// Adds the action to this collector.
    /// </summary>
    /// <param name="action">The set of actions.</param>
    public void Add(P.OrchestratorAction action)
    {
        Check.NotNull(action);

        if (action is { SetStatus: { } a })
        {
            this.SubStatus = a.Status;
        }
        else
        {
            this.actions.Add(action);
        }
    }

    /// <inheritdoc/>
    public void Clear()
    {
        this.SubStatus = null;
        this.actions.Clear();
    }

    /// <inheritdoc/>
    public bool Contains(P.OrchestratorAction item)
    {
        return this.actions.Contains(item);
    }

    /// <inheritdoc/>
    public void CopyTo(P.OrchestratorAction[] array, int arrayIndex)
    {
        this.actions.CopyTo(array, arrayIndex);
    }

    /// <inheritdoc/>
    public bool Remove(P.OrchestratorAction item)
    {
        return this.actions.Remove(item);
    }

    /// <inheritdoc/>
    public IEnumerator<P.OrchestratorAction> GetEnumerator()
    {
        return ((IEnumerable<P.OrchestratorAction>)this.actions).GetEnumerator();
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable)this.actions).GetEnumerator();
    }
}
