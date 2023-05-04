// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Marks a message as an action an orchestrator can take.
/// </summary>
interface IOrchestrationAction
{
}

/// <summary>
/// Represents a piece of the saved state of an <see cref="ITaskOrchestrator" />.
/// </summary>
/// <param name="Id">The ID of the message.</param>
public abstract record OrchestrationMessage(int Id);

/// <summary>
/// <see cref="ITaskOrchestrator" /> has scheduled some form of work to be performed.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="Name">The name of the work to run.</param>
/// <param name="Input">The input for the work.</param>
public abstract record WorkScheduledMessage(int Id, TaskName Name, string? Input)
    : OrchestrationMessage(Id), IOrchestrationAction;

/// <summary>
/// A previously scheduled work item has completed - successfully or failed.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="ScheduledId">The ID of the corresponding <see cref="WorkScheduledMessage" /> message.</param>
/// <param name="Result">The result of the scheduled work.</param>
/// <param name="Failure">The failure details for a failed scheduled work.</param>
public abstract record WorkCompletedMessage(
    int Id, int ScheduledId, string? Result, TaskFailureDetails? Failure)
    : OrchestrationMessage(Id);

/// <summary>
/// <see cref="ITaskOrchestrator" /> execution started message.
/// </summary>
/// <param name="Input">The serialized orchestration input.</param>
public record ExecutionStarted(string? Input)
    : OrchestrationMessage(-1);

/// <summary>
/// <see cref="ITaskOrchestrator" /> has completed execution.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="Result">The result of the <see cref="ITaskOrchestrator" />.</param>
/// <param name="Failure">The failure details for a failed <see cref="ITaskActivity" />.</param>
public record ExecutionCompleted(int Id, string? Result, TaskFailureDetails? Failure)
    : OrchestrationMessage(Id), IOrchestrationAction;

/// <summary>
/// <see cref="ITaskOrchestrator" /> has requested to be "continued-as-new".
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="NewInput">The new input for the next orchestration run.</param>
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix. Justification: not a suffix, but a concept.
public record ContinueAsNew(int Id, string? NewInput)
    : ExecutionCompleted(Id, NewInput, null);
#pragma warning restore CA1711

/// <summary>
/// <see cref="ITaskOrchestrator" /> has sent an event to another <see cref="ITaskOrchestrator" />.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="InstanceId">The instance ID of the receiving orchestration.</param>
/// <param name="Name">The name of the event.</param>
/// <param name="Input">The serialized event input.</param>
public record EventSent(int Id, string InstanceId, string Name, string? Input)
    : OrchestrationMessage(Id), IOrchestrationAction;

/// <summary>
/// <see cref="ITaskOrchestrator" /> has received an event.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="Name">The name of the event.</param>
/// <param name="Input">The serialized event input.</param>
public record EventReceived(int Id, string Name, string? Input)
    : OrchestrationMessage(Id);

/// <summary>
/// <see cref="ITaskOrchestrator" /> created a new timer.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="FireAt">The time at which the timer should fire.</param>
public record TimerCreated(int Id, DateTimeOffset FireAt)
    : OrchestrationMessage(Id), IOrchestrationAction;

/// <summary>
/// A previously created timer has fired.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="CreatedId">The ID of the corresponding <see cref="TimerCreated" /> message.</param>
public record TimerFired(int Id, int CreatedId)
    : OrchestrationMessage(Id);

/// <summary>
/// <see cref="ITaskOrchestrator" /> has scheduled an <see cref="ITaskActivity" /> to run.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="Name">The name of the <see cref="ITaskActivity" /> to run.</param>
/// <param name="Input">The input for the <see cref="ITaskActivity" />.</param>
public record TaskActivityScheduled(int Id, TaskName Name, string? Input)
    : WorkScheduledMessage(Id, Name, Input);

/// <summary>
/// A previously scheduled <see cref="ITaskActivity" /> has completed - successfully or failed.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="ScheduledId">The ID of the corresponding <see cref="TaskActivityScheduled" /> message.</param>
/// <param name="Result">The result of the <see cref="ITaskActivity" />.</param>
/// <param name="Failure">The failure details for a failed <see cref="ITaskActivity" />.</param>
public record TaskActivityCompleted(
    int Id, int ScheduledId, string? Result, TaskFailureDetails? Failure)
    : WorkCompletedMessage(Id, ScheduledId, Result, Failure);

/// <summary>
/// <see cref="ITaskOrchestrator" /> has scheduled a sub <see cref="ITaskOrchestrator" /> to run.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="Name">The name of the <see cref="ITaskOrchestrator" /> to run.</param>
/// <param name="Input">The input for the <see cref="ITaskOrchestrator" />.</param>
/// <param name="Options">The options to schedule the <see cref="ITaskOrchestrator" /> with.</param>
public record SubOrchestrationScheduled(
    int Id, TaskName Name, string? Input, SubOrchestrationScheduledOptions? Options)
    : WorkScheduledMessage(Id, Name, Input);

/// <summary>
/// A previously scheduled sub <see cref="ITaskOrchestrator" /> has completed - successfully or failed.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="ScheduledId">The ID of the corresponding <see cref="TaskActivityScheduled" /> message.</param>
/// <param name="Result">The result of the <see cref="ITaskOrchestrator" />.</param>
/// <param name="Failure">The failure details for a failed <see cref="ITaskOrchestrator" />.</param>
public record SubOrchestrationCompleted(
    int Id, int ScheduledId, string? Result, TaskFailureDetails? Failure)
    : WorkCompletedMessage(Id, ScheduledId, Result, Failure);

/// <summary>
/// Options that may be provided when scheduled a sub <see cref="ITaskOrchestrator" />.
/// </summary>
/// <param name="InstanceId">The sub-orchestrations instance ID.</param>
public record SubOrchestrationScheduledOptions(string InstanceId);
