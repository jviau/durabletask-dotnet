// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using DurableTask.Core;
using DurableTask.Core.History;

namespace Microsoft.DurableTask.Worker.OrchestrationServiceShim;

/// <summary>
/// Extensions for DurableTask.Core types.
/// </summary>
static class DurableTaskCoreExtensions
{
    /// <summary>
    /// Gets the <see cref="OrchestrationStatus"/> from a <see cref="ExecutionCompleted"/>.
    /// </summary>
    /// <param name="completed">The completed event.</param>
    /// <returns>The orchestration status.</returns>
    public static OrchestrationStatus GetStatus(this ExecutionCompleted completed)
    {
        Check.NotNull(completed);
        return completed switch
        {
            ContinueAsNew => OrchestrationStatus.ContinuedAsNew,
            { Failure: not null } => OrchestrationStatus.Failed,
            _ => OrchestrationStatus.Completed,
        };
    }

    /// <summary>
    /// Convert <see cref="FailureDetails" /> to <see cref="TaskFailureDetails" />.
    /// </summary>
    /// <param name="details">The details to convert.</param>
    /// <returns>The task failure details.</returns>
    [return: NotNullIfNotNull("details")]
    public static TaskFailureDetails? ConvertFromCore(this FailureDetails? details)
    {
        if (details is null)
        {
            return null;
        }

        TaskFailureDetails? inner = details.InnerFailure?.ConvertFromCore();
        return new TaskFailureDetails(details.ErrorType, details.ErrorMessage, details.StackTrace, inner);
    }

    /// <summary>
    /// Converts a <see cref="TaskFailureDetails"/> to a <see cref="FailureDetails"/>.
    /// </summary>
    /// <param name="details">The details to convert.</param>
    /// <returns>The converted details.</returns>
    public static FailureDetails? ToCore(this TaskFailureDetails? details)
    {
        if (details is null)
        {
            return null;
        }

        FailureDetails? inner = details.InnerFailure?.ToCore();
        return new FailureDetails(details.ErrorType, details.ErrorMessage, details.StackTrace, inner, false);
    }

    /// <summary>
    /// Gets the <see cref="ParentOrchestrationInstance"/> for a <see cref="TaskOrchestrationWorkItem"/>.
    /// </summary>
    /// <param name="orchestration">The orchestration work item.</param>
    /// <returns>The parent orchestration instance, or <c>null</c> if no parent.</returns>
    public static ParentOrchestrationInstance? GetParent(this TaskOrchestrationWorkItem orchestration)
    {
        Check.NotNull(orchestration);
        if (orchestration.OrchestrationRuntimeState.ParentInstance is not { } p)
        {
            return null;
        }

        return new(p.Name, p.OrchestrationInstance.InstanceId);
    }

    /// <summary>
    /// Gets a <see cref="SubOrchestrationScheduledOptions"/> from a <see cref="SubOrchestrationInstanceCreatedEvent"/>.
    /// </summary>
    /// <param name="event">The sub orchestration created event.</param>
    /// <returns>The options to schedule the sub orchestration with.</returns>
    public static SubOrchestrationScheduledOptions? GetOptions(this SubOrchestrationInstanceCreatedEvent @event)
    {
        Check.NotNull(@event);
        if (@event.InstanceId is { } id)
        {
            return new(id);
        }

        return null;
    }

    /// <summary>
    /// Gets a <see cref="TaskName"/> from a <see cref="SubOrchestrationInstanceCreatedEvent"/>.
    /// </summary>
    /// <param name="event">The sub orchestration created event.</param>
    /// <returns>The task name.</returns>
    public static TaskName GetName(this SubOrchestrationInstanceCreatedEvent @event)
    {
        Check.NotNull(@event);
        return @event.Name is null ? default : new(@event.Name);
    }

    /// <summary>
    /// Gets a <see cref="TaskName"/> from a <see cref="TaskScheduledEvent"/>.
    /// </summary>
    /// <param name="event">The task scheduled event.</param>
    /// <returns>The task name.</returns>
    public static TaskName GetName(this TaskScheduledEvent @event)
    {
        Check.NotNull(@event);
        return @event.Name is null ? default : new(@event.Name);
    }

    /// <summary>
    /// Gets the <see cref="TaskName"/> for a <see cref="TaskOrchestrationWorkItem"/>.
    /// </summary>
    /// <param name="orchestration">The orchestration work item.</param>
    /// <returns>The task name.</returns>
    public static TaskName GetName(this TaskOrchestrationWorkItem orchestration)
    {
        Check.NotNull(orchestration);
        return orchestration.OrchestrationRuntimeState.Name;
    }

    /// <summary>
    /// Gets the <see cref="TaskName"/> for a <see cref="TaskActivityWorkItem"/>.
    /// </summary>
    /// <param name="activity">The activity work item.</param>
    /// <returns>The task name.</returns>
    public static TaskName GetName(this TaskActivityWorkItem activity)
    {
        Check.NotNull(activity);
        if (activity.TaskMessage.Event is not TaskScheduledEvent e
            || e.Name is not string name)
        {
            throw new InvalidOperationException("Provided work item is not the correct shape.");
        }

        return name;
    }

    /// <summary>
    /// Gets the input for a <see cref="TaskActivityWorkItem"/>.
    /// </summary>
    /// <param name="activity">The activity work item.</param>
    /// <returns>The task input.</returns>
    public static string? GetInput(this TaskActivityWorkItem activity)
    {
        Check.NotNull(activity);
        if (activity.TaskMessage.Event is not TaskScheduledEvent e)
        {
            throw new InvalidOperationException("Provided work item is not the correct shape.");
        }

        return e.Input;
    }

    /// <summary>
    /// Gets the task ID for a <see cref="TaskActivityWorkItem"/>.
    /// </summary>
    /// <param name="activity">The activity work item.</param>
    /// <returns>The task ID.</returns>
    public static int GetTaskId(this TaskActivityWorkItem activity)
    {
        Check.NotNull(activity);
        if (activity.TaskMessage.Event is not TaskScheduledEvent e)
        {
            throw new InvalidOperationException("Provided work item is not the correct shape.");
        }

        return e.EventId;
    }

    /// <summary>
    /// Try to get the scheduled time for a message.
    /// </summary>
    /// <param name="message">The task message.</param>
    /// <param name="delay">The delay before scheduling.</param>
    /// <returns>True if schedule time present, false otherwise.</returns>
    public static bool TryGetScheduledTime(this TaskMessage message, out TimeSpan delay)
    {
        Check.NotNull(message);

        DateTime scheduledTime = default;
        if (message.Event is ExecutionStartedEvent startEvent)
        {
            scheduledTime = startEvent.ScheduledStartTime ?? default;
        }
        else if (message.Event is TimerFiredEvent timerEvent)
        {
            scheduledTime = timerEvent.FireAt;
        }

        DateTime now = DateTime.UtcNow;
        if (scheduledTime > now)
        {
            delay = scheduledTime - now;
            return true;
        }
        else
        {
            delay = default;
            return false;
        }
    }

    /// <summary>
    /// Prepare this work item for execution.
    /// </summary>
    /// <param name="workItem">The work item to run.</param>
    public static void PrepareForRun(this TaskOrchestrationWorkItem workItem)
    {
        Check.NotNull(workItem);

        workItem.OrchestrationRuntimeState.AddEvent(new OrchestratorStartedEvent(-1));
        foreach (TaskMessage message in workItem.FilterAndSortMessages())
        {
            workItem.OrchestrationRuntimeState.AddEvent(message.Event);
        }
    }

    /// <summary>
    /// Filter and sort the messages from a work item.
    /// </summary>
    /// <param name="workItem">The work item to filter sort.</param>
    /// <returns>Enumerable of task messages.</returns>
    public static IEnumerable<TaskMessage> FilterAndSortMessages(this TaskOrchestrationWorkItem workItem)
    {
        Check.NotNull(workItem);

        // Group messages by their instance ID
        static string GetGroupingKey(TaskMessage msg) => msg.OrchestrationInstance.InstanceId;

        // Within a group, put messages with a non-null execution ID first
        static int GetSortOrderWithinGroup(TaskMessage msg)
        {
            if (msg.Event.EventType == EventType.ExecutionStarted)
            {
                // Prioritize ExecutionStarted messages
                return 0;
            }
            else if (msg.OrchestrationInstance.ExecutionId != null)
            {
                // Prioritize messages with an execution ID
                return 1;
            }
            else
            {
                return 2;
            }
        }

        string? executionId = workItem.OrchestrationRuntimeState?.OrchestrationInstance?.ExecutionId;

        foreach (IGrouping<string, TaskMessage> group in workItem.NewMessages.GroupBy(GetGroupingKey))
        {
            // TODO: Filter out invalid messages (wrong execution ID, duplicate start/complete messages, etc.)
            foreach (TaskMessage msg in group.OrderBy(GetSortOrderWithinGroup))
            {
                yield return msg;
            }
        }
    }

    /// <summary>
    /// Checks if two orchestration instances equal one another.
    /// </summary>
    /// <param name="left">The left instance to check for equality.</param>
    /// <param name="right">The right instance to check for equality.</param>
    /// <param name="exact">
    /// <c>true</c> to include <see cref="OrchestrationInstance.ExecutionId"/>, <c>false</c> to ignore it.
    /// </param>
    /// <returns>True if equals, false otherwise.</returns>
    public static bool Equals(
        this OrchestrationInstance left, OrchestrationInstance right, bool exact = false)
    {
        Check.NotNull(left);

        if (right is null)
        {
            return false;
        }

        return left.InstanceId.Equals(right.InstanceId, StringComparison.Ordinal)
            && (!exact || left.ExecutionId.Equals(right.ExecutionId, StringComparison.Ordinal));
    }
}
