// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.Command;
using DurableTask.Core.History;

namespace Microsoft.DurableTask.Grpc.Hub.Bulk;

/// <summary>
/// Extensions for <see cref="OrchestratorExecutionResult"/>.
/// </summary>
static class OrchestrationExecutionResultExtensions
{
    /// <summary>
    /// Apply all the actions in this collection to a <see cref="OrchestrationRuntimeState"/>.
    /// </summary>
    /// <param name="result">The <see cref="OrchestratorExecutionResult"/>.</param>
    /// <param name="runtimeState">The runtime state. Modified in most cases, or replaced with a new instance on ContinueAsNew.</param>
    /// <param name="activityMessages">The new activity messages.</param>
    /// <param name="orchestratorMessages">The new orchestrator messages.</param>
    /// <param name="timerMessages">The new timer messages.</param>
    /// <param name="updatedStatus">The updated orchestration state.</param>
    /// <param name="continueAsNew">True if continue as new, false otherwise.</param>
    public static void ApplyActions(
        this OrchestratorExecutionResult result,
        ref OrchestrationRuntimeState runtimeState,
        out IList<TaskMessage> activityMessages,
        out IList<TaskMessage> orchestratorMessages,
        out IList<TaskMessage> timerMessages,
        out OrchestrationState? updatedStatus,
        out bool continueAsNew)
    {
        if (string.IsNullOrEmpty(runtimeState.OrchestrationInstance?.InstanceId))
        {
            throw new ArgumentException($"The provided {nameof(OrchestrationRuntimeState)} doesn't contain an instance ID!", nameof(runtimeState));
        }

        IList<TaskMessage>? newActivityMessages = null;
        IList<TaskMessage>? newTimerMessages = null;
        IList<TaskMessage>? newOrchestratorMessages = null;
        FailureDetails? failureDetails = null;
        continueAsNew = false;

        runtimeState.Status = result.CustomStatus;

        foreach (OrchestratorAction action in result.Actions)
        {
            // TODO: Determine how to handle remaining actions if the instance completed with ContinueAsNew.
            // TODO: Validate each of these actions to make sure they have the appropriate data.
            if (action is ScheduleTaskOrchestratorAction scheduleTaskAction)
            {
                if (string.IsNullOrEmpty(scheduleTaskAction.Name))
                {
                    throw new ArgumentException($"The provided {nameof(ScheduleTaskOrchestratorAction)} has no Name property specified!", nameof(result));
                }

                TaskScheduledEvent scheduledEvent = new(
                    scheduleTaskAction.Id,
                    scheduleTaskAction.Name,
                    scheduleTaskAction.Version,
                    scheduleTaskAction.Input);

                newActivityMessages ??= new List<TaskMessage>();
                newActivityMessages.Add(new TaskMessage
                {
                    Event = scheduledEvent,
                    OrchestrationInstance = runtimeState.OrchestrationInstance,
                });

                runtimeState.AddEvent(scheduledEvent);
            }
            else if (action is CreateTimerOrchestratorAction timerAction)
            {
                TimerCreatedEvent timerEvent = new(timerAction.Id, timerAction.FireAt);

                newTimerMessages ??= new List<TaskMessage>();
                newTimerMessages.Add(new TaskMessage
                {
                    Event = new TimerFiredEvent(-1, timerAction.FireAt)
                    {
                        TimerId = timerAction.Id,
                    },
                    OrchestrationInstance = runtimeState.OrchestrationInstance,
                });

                runtimeState.AddEvent(timerEvent);
            }
            else if (action is CreateSubOrchestrationAction subOrchestrationAction)
            {
                runtimeState.AddEvent(new SubOrchestrationInstanceCreatedEvent(subOrchestrationAction.Id)
                {
                    Name = subOrchestrationAction.Name,
                    Version = subOrchestrationAction.Version,
                    InstanceId = subOrchestrationAction.InstanceId,
                    Input = subOrchestrationAction.Input,
                });

                ExecutionStartedEvent startedEvent = new(-1, subOrchestrationAction.Input)
                {
                    Name = subOrchestrationAction.Name,
                    Version = subOrchestrationAction.Version,
                    OrchestrationInstance = new OrchestrationInstance
                    {
                        InstanceId = subOrchestrationAction.InstanceId,
                        ExecutionId = Guid.NewGuid().ToString("N"),
                    },
                    ParentInstance = new ParentInstance
                    {
                        OrchestrationInstance = runtimeState.OrchestrationInstance,
                        Name = runtimeState.Name,
                        Version = runtimeState.Version,
                        TaskScheduleId = subOrchestrationAction.Id,
                    },
                    Tags = subOrchestrationAction.Tags,
                };

                newOrchestratorMessages ??= new List<TaskMessage>();
                newOrchestratorMessages.Add(new TaskMessage
                {
                    Event = startedEvent,
                    OrchestrationInstance = startedEvent.OrchestrationInstance,
                });
            }
            else if (action is SendEventOrchestratorAction sendEventAction)
            {
                if (string.IsNullOrEmpty(sendEventAction.Instance?.InstanceId))
                {
                    throw new ArgumentException($"The provided {nameof(SendEventOrchestratorAction)} doesn't contain an instance ID!");
                }

                EventSentEvent sendEvent = new(sendEventAction.Id)
                {
                    InstanceId = sendEventAction.Instance.InstanceId,
                    Name = sendEventAction.EventName,
                    Input = sendEventAction.EventData,
                };

                runtimeState.AddEvent(sendEvent);

                newOrchestratorMessages ??= new List<TaskMessage>();
                newOrchestratorMessages.Add(new TaskMessage
                {
                    Event = sendEvent,
                    OrchestrationInstance = runtimeState.OrchestrationInstance,
                });
            }
            else if (action is OrchestrationCompleteOrchestratorAction completeAction)
            {
                if (completeAction.OrchestrationStatus == OrchestrationStatus.ContinuedAsNew)
                {
                    // Replace the existing runtime state with a complete new runtime state.
                    OrchestrationRuntimeState newRuntimeState = new();
                    newRuntimeState.AddEvent(new OrchestratorStartedEvent(-1));
                    newRuntimeState.AddEvent(new ExecutionStartedEvent(-1, completeAction.Result)
                    {
                        OrchestrationInstance = new OrchestrationInstance
                        {
                            InstanceId = runtimeState.OrchestrationInstance.InstanceId,
                            ExecutionId = Guid.NewGuid().ToString("N"),
                        },
                        Tags = runtimeState.Tags,
                        ParentInstance = runtimeState.ParentInstance,
                        Name = runtimeState.Name,
                        Version = completeAction.NewVersion ?? runtimeState.Version,
                    });
                    newRuntimeState.Status = runtimeState.Status;

                    // The orchestration may have completed with some pending events that need to be carried
                    // over to the new generation, such as unprocessed external event messages.
                    if (completeAction.CarryoverEvents != null)
                    {
                        foreach (HistoryEvent carryoverEvent in completeAction.CarryoverEvents)
                        {
                            newRuntimeState.AddEvent(carryoverEvent);
                        }
                    }

                    runtimeState = newRuntimeState;
                    activityMessages = Array.Empty<TaskMessage>();
                    orchestratorMessages = Array.Empty<TaskMessage>();
                    timerMessages = Array.Empty<TaskMessage>();
                    continueAsNew = true;
                    updatedStatus = null;
                    return;
                }

                if (completeAction.OrchestrationStatus == OrchestrationStatus.Failed)
                {
                    failureDetails = completeAction.FailureDetails;
                }

                // NOTE: Failure details aren't being stored in the orchestration history, currently.
                runtimeState.AddEvent(new ExecutionCompletedEvent(
                    completeAction.Id,
                    completeAction.Result,
                    completeAction.OrchestrationStatus));

                // CONSIDER: Add support for fire-and-forget sub-orchestrations where
                //           we don't notify the parent that the orchestration completed.
                if (runtimeState.ParentInstance != null)
                {
                    HistoryEvent subOrchestratorCompletedEvent;
                    if (completeAction.OrchestrationStatus == OrchestrationStatus.Completed)
                    {
                        subOrchestratorCompletedEvent = new SubOrchestrationInstanceCompletedEvent(
                            eventId: -1,
                            runtimeState.ParentInstance.TaskScheduleId,
                            completeAction.Result);
                    }
                    else
                    {
                        subOrchestratorCompletedEvent = new SubOrchestrationInstanceFailedEvent(
                            eventId: -1,
                            runtimeState.ParentInstance.TaskScheduleId,
                            completeAction.Result,
                            completeAction.Details,
                            completeAction.FailureDetails);
                    }

                    newOrchestratorMessages ??= new List<TaskMessage>();
                    newOrchestratorMessages.Add(new TaskMessage
                    {
                        Event = subOrchestratorCompletedEvent,
                        OrchestrationInstance = runtimeState.ParentInstance.OrchestrationInstance,
                    });
                }
            }
        }

        runtimeState.AddEvent(new OrchestratorCompletedEvent(-1));

        activityMessages = newActivityMessages ?? Array.Empty<TaskMessage>();
        timerMessages = newTimerMessages ?? Array.Empty<TaskMessage>();
        orchestratorMessages = newOrchestratorMessages ?? Array.Empty<TaskMessage>();

        updatedStatus = new OrchestrationState
        {
            OrchestrationInstance = runtimeState.OrchestrationInstance,
            ParentInstance = runtimeState.ParentInstance,
            Name = runtimeState.Name,
            Version = runtimeState.Version,
            Status = runtimeState.Status,
            Tags = runtimeState.Tags,
            OrchestrationStatus = runtimeState.OrchestrationStatus,
            CreatedTime = runtimeState.CreatedTime,
            CompletedTime = runtimeState.CompletedTime,
            LastUpdatedTime = DateTime.UtcNow,
            Size = runtimeState.Size,
            CompressedSize = runtimeState.CompressedSize,
            Input = runtimeState.Input,
            Output = runtimeState.Output,
            ScheduledStartTime = runtimeState.ExecutionStartedEvent?.ScheduledStartTime,
            FailureDetails = failureDetails,
        };
    }
}
