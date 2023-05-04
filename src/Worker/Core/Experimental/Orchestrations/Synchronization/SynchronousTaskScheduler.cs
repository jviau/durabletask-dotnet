// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// A task scheduler which always posts continuations inline.
/// </summary>
class SynchronousTaskScheduler : TaskScheduler
{
    /// <inheritdoc/>
    public override int MaximumConcurrencyLevel => 1;

    /// <inheritdoc/>
    protected override void QueueTask(Task task)
        => this.TryExecuteTask(task);

    /// <inheritdoc/>
    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        => this.TryExecuteTask(task);

    /// <inheritdoc/>
    protected override IEnumerable<Task> GetScheduledTasks()
        => Enumerable.Empty<Task>();
}
