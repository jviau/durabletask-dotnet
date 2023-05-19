// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Represents an <see cref="ITaskActivity" /> work item to run.
/// </summary>
public abstract class ActivityWorkItem : WorkItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityWorkItem"/> class.
    /// </summary>
    /// <param name="id">The ID of the activity.</param>
    /// <param name="name">The name of the activity.</param>
    protected ActivityWorkItem(string id, TaskName name)
        : base(id, name)
    {
    }

    /// <summary>
    /// Gets the input for this work item.
    /// </summary>
    public abstract string? Input { get; }

    /// <summary>
    /// Completes this work item.
    /// </summary>
    /// <param name="result">The result of the activity.</param>
    /// <returns>A task that completes when the activity has been completed.</returns>
    public abstract ValueTask CompleteAsync(string? result);

    /// <summary>
    /// Fails this work item.
    /// </summary>
    /// <param name="exception">The work item exception.</param>
    /// <returns>A task that completes when the activity has been failed.</returns>
    public abstract ValueTask FailAsync(Exception exception);
}
