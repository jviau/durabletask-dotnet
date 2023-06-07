// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.History;

namespace Microsoft.DurableTask.Grpc.Hub;

/// <summary>
/// Extensions for DurableTask.Core types.
/// </summary>
static class DurableTaskCoreExtensions
{
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

        workItem.OrchestrationRuntimeState.NewEvents.Insert(0, new OrchestratorStartedEvent(-1));
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
