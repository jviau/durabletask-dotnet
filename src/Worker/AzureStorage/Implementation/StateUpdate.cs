// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// Orchestration runtime status. Copy from Microsoft.DurableTask.Client.
/// </summary>
enum RuntimeStatus
{
    /// <summary>
    /// The orchestration started running.
    /// </summary>
    Running,

    /// <summary>
    /// The orchestration completed normally.
    /// </summary>
    Completed,

    /// <summary>
    /// The orchestration is transitioning into a new instance.
    /// </summary>
    [Obsolete("The ContinuedAsNew status is obsolete and exists only for compatibility reasons.")]
    ContinuedAsNew,

    /// <summary>
    /// The orchestration completed with an unhandled exception.
    /// </summary>
    Failed,

    /// <summary>
    /// The orchestration canceled gracefully.
    /// </summary>
    [Obsolete("The Canceled status is not currently used and exists only for compatibility reasons.")]
    Canceled,

    /// <summary>
    /// The orchestration was abruptly terminated via a management API call.
    /// </summary>
    Terminated,

    /// <summary>
    /// The orchestration was scheduled but hasn't started running.
    /// </summary>
    Pending,

    /// <summary>
    /// The orchestration has been suspended.
    /// </summary>
    Suspended,
}

/// <summary>
/// Status for an orchestration.
/// </summary>
record class StateUpdate
{
    /// <summary>
    /// Gets the runtime status.
    /// </summary>
    public RuntimeStatus Status { get; init; }

    /// <summary>
    /// Gets the sub-status of the orchestration.
    /// </summary>
    public Optional<string?> SubStatus { get; init; }

    /// <summary>
    /// Gets the result of the orchestration, if available.
    /// </summary>
    public string? Result { get; init; }

    /// <summary>
    /// Gets the orchestration failure, if available.
    /// </summary>
    public TaskFailureDetails? Failure { get; init; }
}
