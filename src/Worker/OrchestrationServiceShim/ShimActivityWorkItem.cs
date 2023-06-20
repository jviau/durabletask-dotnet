// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.Core.History;

namespace Microsoft.DurableTask.Worker.OrchestrationServiceShim;

/// <summary>
/// <see cref="ActivityWorkItem"/> backed by a <see cref="TaskActivityWorkItem"/>.
/// </summary>
class ShimActivityWorkItem : ActivityWorkItem
{
    readonly IOrchestrationService service;
    readonly TaskActivityWorkItem inner;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShimActivityWorkItem"/> class.
    /// </summary>
    /// <param name="service">The orchestration service.</param>
    /// <param name="inner">The work item.</param>
    public ShimActivityWorkItem(IOrchestrationService service, TaskActivityWorkItem inner)
        : base(Check.NotNull(inner).Id, inner.GetName())
    {
        this.service = Check.NotNull(service);
        this.inner = Check.NotNull(inner);
    }

    /// <inheritdoc/>
    public override string? Input => this.inner.GetInput();

    /// <inheritdoc/>
    public override ValueTask CompleteAsync(string? result)
    {
        return this.CompleteAsync(new TaskCompletedEvent(-1, this.inner.GetTaskId(), result));
    }

    /// <inheritdoc/>
    public override ValueTask FailAsync(Exception exception)
    {
        return this.CompleteAsync(new TaskFailedEvent(
            -1, this.inner.GetTaskId(), null, null, new FailureDetails(exception)));
    }

    ValueTask CompleteAsync(HistoryEvent @event)
    {
        TaskMessage message = new()
        {
            Event = @event,
            OrchestrationInstance = this.inner.TaskMessage.OrchestrationInstance,
        };

        return new(this.service.CompleteTaskActivityWorkItemAsync(this.inner, message));
    }
}
