﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Enum describing the runtime status of the orchestration.
/// </summary>
public enum OrchestrationRuntimeStatus
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
/// Extensions for <see cref="OrchestrationRuntimeStatus"/>.
/// </summary>
public static class OrchestrationRuntimeStatusExtensions
{
    /// <summary>
    /// Checks in a <see cref="OrchestrationRuntimeStatus"/> is a terminal state.
    /// </summary>
    /// <param name="status">The status to check.</param>
    /// <returns>True if terminal, false otherwise.</returns>
    public static bool IsTerminal(this OrchestrationRuntimeStatus status)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        return status is OrchestrationRuntimeStatus.Completed
            or OrchestrationRuntimeStatus.Terminated
            or OrchestrationRuntimeStatus.Canceled
            or OrchestrationRuntimeStatus.Failed;
#pragma warning restore CS0618 // Type or member is obsolete
    }
}
