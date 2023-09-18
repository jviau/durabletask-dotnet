// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Represents a piece of the saved state of an <see cref="ITaskOrchestrator" />.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="Timestamp">The timestamp this message originally occured at.</param>
public abstract record OrchestrationMessage(int Id, DateTimeOffset Timestamp);

/// <summary>
/// <see cref="ITaskOrchestrator" /> has scheduled some form of work to be performed.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="Timestamp">The timestamp this message originally occured at.</param>
/// <param name="Name">The name of the work to run.</param>
/// <param name="Input">The input for the work.</param>
public abstract record WorkScheduledMessage(int Id, DateTimeOffset Timestamp, TaskName Name, string? Input)
    : OrchestrationMessage(Id, Timestamp);

/// <summary>
/// A previously scheduled work item has completed - successfully or failed.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="Timestamp">The timestamp this message originally occured at.</param>
/// <param name="ScheduledId">The ID of the corresponding <see cref="WorkScheduledMessage" /> message.</param>
/// <param name="Result">The result of the scheduled work.</param>
/// <param name="Failure">The failure details for a failed scheduled work.</param>
public abstract record WorkCompletedMessage(
    int Id, DateTimeOffset Timestamp, int ScheduledId, string? Result, TaskFailureDetails? Failure)
    : OrchestrationMessage(Id, Timestamp);

/// <summary>
/// A generic message with an opaque name and payload.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="Timestamp">The timestamp this message originally occured at.</param>
/// <param name="Name">The name of the message.</param>
/// <param name="Data">The payload of the message.</param>
public record GenericMessage(int Id, DateTimeOffset Timestamp, string Name, string? Data)
    : OrchestrationMessage(Id, Timestamp);

/// <summary>
/// The orchestrator has been started (or restarted in the case of a replay).
/// </summary>
/// <param name="Timestamp">The timestamp the replay has started at.</param>
public record OrchestratorStarted(DateTimeOffset Timestamp)
    : OrchestrationMessage(-1, Timestamp);

/// <summary>
/// <see cref="ITaskOrchestrator" /> execution started message.
/// </summary>
/// <param name="Timestamp">The timestamp this message originally occured at.</param>
/// <param name="Input">The serialized orchestration input.</param>
public record ExecutionStarted(DateTimeOffset Timestamp, string? Input)
    : OrchestrationMessage(-1, Timestamp);

/// <summary>
/// <see cref="ITaskOrchestrator" /> has completed execution.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="Timestamp">The timestamp this message originally occured at.</param>
/// <param name="Result">The result of the <see cref="ITaskOrchestrator" />.</param>
/// <param name="Failure">The failure details for a failed <see cref="ITaskActivity" />.</param>
public record ExecutionCompleted(int Id, DateTimeOffset Timestamp, string? Result, TaskFailureDetails? Failure)
    : OrchestrationMessage(Id, Timestamp);

/// <summary>
/// <see cref="ITaskOrchestrator" /> has been forcibly terminated.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="Timestamp">The timestamp this message originally occured at.</param>
/// <param name="Result">The reason for termination.</param>
/// <remarks>
/// This message plays double duty as both an inbound termination signal and an outbound completion message.
/// </remarks>
public record ExecutionTerminated(int Id, DateTimeOffset Timestamp, string? Result)
    : ExecutionCompleted(Id, Timestamp, Result, null);

/// <summary>
/// <see cref="ITaskOrchestrator" /> has requested to be "continued-as-new".
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="Timestamp">The timestamp this message originally occured at.</param>
/// <param name="Result">The new input for the next orchestration run.</param>
/// <param name="Version">The new version for the next iteration.</param>
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix. Justification: not a suffix, but a concept.
public record ContinueAsNew(int Id, DateTimeOffset Timestamp, string? Result, string? Version = null)
    : ExecutionCompleted(Id, Timestamp, Result, null)
{
    /// <summary>
    /// Gets the list of carry over messages.
    /// </summary>
    public List<OrchestrationMessage> CarryOverMessages { get; } = new();
}
#pragma warning restore CA1711

/// <summary>
/// <see cref="ITaskOrchestrator" /> has sent an event to another <see cref="ITaskOrchestrator" />.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="Timestamp">The timestamp this message originally occured at.</param>
/// <param name="InstanceId">The instance ID of the receiving orchestration.</param>
/// <param name="Name">The name of the event.</param>
/// <param name="Input">The serialized event input.</param>
public record EventSent(int Id, DateTimeOffset Timestamp, string InstanceId, string Name, string? Input)
    : OrchestrationMessage(Id, Timestamp);

/// <summary>
/// <see cref="ITaskOrchestrator" /> has received an event.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="Timestamp">The timestamp this message originally occured at.</param>
/// <param name="Name">The name of the event.</param>
/// <param name="Input">The serialized event input.</param>
public record EventReceived(int Id, DateTimeOffset Timestamp, string Name, string? Input)
    : OrchestrationMessage(Id, Timestamp);

/// <summary>
/// <see cref="ITaskOrchestrator" /> created a new timer.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="Timestamp">The timestamp this message originally occured at.</param>
/// <param name="FireAt">The time at which the timer should fire.</param>
public record TimerScheduled(int Id, DateTimeOffset Timestamp, DateTimeOffset FireAt)
    : OrchestrationMessage(Id, Timestamp);

/// <summary>
/// A previously created timer has fired.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="Timestamp">The timestamp this message originally occured at.</param>
/// <param name="ScheduledId">The ID of the corresponding <see cref="TimerScheduled" /> message.</param>
public record TimerFired(int Id, DateTimeOffset Timestamp, int ScheduledId)
    : OrchestrationMessage(Id, Timestamp);

/// <summary>
/// <see cref="ITaskOrchestrator" /> has scheduled an <see cref="ITaskActivity" /> to run.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="Timestamp">The timestamp this message originally occured at.</param>
/// <param name="Name">The name of the <see cref="ITaskActivity" /> to run.</param>
/// <param name="Input">The input for the <see cref="ITaskActivity" />.</param>
/// <param name="Options">The options to schedule this <see cref="ITaskActivity"/> with.</param>
public record TaskActivityScheduled(
    int Id, DateTimeOffset Timestamp, TaskName Name, string? Input, TaskScheduledOptions? Options = null)
    : WorkScheduledMessage(Id, Timestamp, Name, Input);

/// <summary>
/// A previously scheduled <see cref="ITaskActivity" /> has completed - successfully or failed.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="Timestamp">The timestamp this message originally occured at.</param>
/// <param name="ScheduledId">The ID of the corresponding <see cref="TaskActivityScheduled" /> message.</param>
/// <param name="Result">The result of the <see cref="ITaskActivity" />.</param>
/// <param name="Failure">The failure details for a failed <see cref="ITaskActivity" />.</param>
public record TaskActivityCompleted(
    int Id, DateTimeOffset Timestamp, int ScheduledId, string? Result, TaskFailureDetails? Failure)
    : WorkCompletedMessage(Id, Timestamp, ScheduledId, Result, Failure);

/// <summary>
/// <see cref="ITaskOrchestrator" /> has scheduled a sub <see cref="ITaskOrchestrator" /> to run.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="Timestamp">The timestamp this message originally occured at.</param>
/// <param name="Name">The name of the <see cref="ITaskOrchestrator" /> to run.</param>
/// <param name="Input">The input for the <see cref="ITaskOrchestrator" />.</param>
/// <param name="Options">The options to schedule the <see cref="ITaskOrchestrator" /> with.</param>
public record SubOrchestrationScheduled(
    int Id, DateTimeOffset Timestamp, TaskName Name, string? Input, SubOrchestrationScheduledOptions? Options = null)
    : WorkScheduledMessage(Id, Timestamp, Name, Input);

/// <summary>
/// A previously scheduled sub <see cref="ITaskOrchestrator" /> has completed - successfully or failed.
/// </summary>
/// <param name="Id">The ID of the message.</param>
/// <param name="Timestamp">The timestamp this message originally occured at.</param>
/// <param name="ScheduledId">The ID of the corresponding <see cref="TaskActivityScheduled" /> message.</param>
/// <param name="Result">The result of the <see cref="ITaskOrchestrator" />.</param>
/// <param name="Failure">The failure details for a failed <see cref="ITaskOrchestrator" />.</param>
public record SubOrchestrationCompleted(
    int Id, DateTimeOffset Timestamp, int ScheduledId, string? Result, TaskFailureDetails? Failure)
    : WorkCompletedMessage(Id, Timestamp, ScheduledId, Result, Failure);

/// <summary>
/// Options that may be provided when scheduled a sub <see cref="ITaskActivity" />.
/// </summary>
/// <param name="InheritMetadata">True to inherit metadata, false otherwise. Defaults to true.</param>
/// <param name="FireAndForget">Fire and forget this orchestration.</param>
public record TaskScheduledOptions(bool InheritMetadata = true, bool FireAndForget = false)
{
    /// <summary>
    /// Gets the sub-orchestrations metadata.
    /// </summary>
    public IDictionary<string, string> Metadata { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Builds the metadata to be used for calling a sub orchestration.
    /// </summary>
    /// <param name="parentMetadata">The metadata of the parent orchestration.</param>
    /// <returns>The built metadata.</returns>
    public IDictionary<string, string> BuildMetadata(IDictionary<string, string>? parentMetadata)
    {
        if (!this.InheritMetadata || parentMetadata is null)
        {
            return this.Metadata;
        }

        return parentMetadata.MergeLeft(this.Metadata);
    }
}

/// <summary>
/// Options that may be provided when scheduled a sub <see cref="ITaskOrchestrator" />.
/// </summary>
/// <param name="InstanceId">The sub-orchestrations instance ID.</param>
/// <param name="InheritMetadata">True to inherit metadata, false otherwise. Defaults to true.</param>
/// <param name="FireAndForget">Fire and forget this orchestration.</param>
public record SubOrchestrationScheduledOptions(
    string? InstanceId = null, bool InheritMetadata = true, bool FireAndForget = false)
    : TaskScheduledOptions(InheritMetadata, FireAndForget);
