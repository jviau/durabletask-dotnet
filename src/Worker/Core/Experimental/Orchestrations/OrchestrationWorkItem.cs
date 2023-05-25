// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Represents an <see cref="ITaskOrchestrator" /> work item to run.
/// </summary>
public abstract class OrchestrationWorkItem : WorkItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestrationWorkItem"/> class.
    /// </summary>
    /// <param name="id">The ID of the orchestration.</param>
    /// <param name="name">The name of the orchestration.</param>
    protected OrchestrationWorkItem(string id, TaskName name)
        : base(id, name)
    {
    }

    /// <summary>
    /// Gets the parent orchestration instance details.
    /// </summary>
    public abstract ParentOrchestrationInstance? Parent { get; }

    /// <summary>
    /// Gets or sets the custom status of this orchestration.
    /// </summary>
    public abstract string? CustomStatus { get; set; }

    /// <summary>
    /// Gets a value indicating whether this channel is currently replaying existing messages.
    /// </summary>
    public abstract bool IsReplaying { get; }

    /// <summary>
    /// Gets the orchestration channel.
    /// </summary>
    public abstract Channel<OrchestrationMessage> Channel { get; }

    /// <summary>
    /// Gets a value indicating whether this orchestration channel is complete or not.
    /// </summary>
    internal bool IsCompleted => this.Channel.Reader.Completion.IsCompleted;

    /// <summary>
    /// Signals this orchestration has finished.
    /// </summary>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task that represents the releasing of this orchestration.</returns>
    public virtual Task ReleaseAsync(CancellationToken cancellation = default) => Task.CompletedTask;
}
