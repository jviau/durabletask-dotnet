// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Contract to run work items.
/// </summary>
/// <typeparam name="T">The work item to run.</typeparam>
public interface IWorkItemRunner<T>
    where T : WorkItem
{
    /// <summary>
    /// Runs the work item.
    /// </summary>
    /// <param name="workItem">The work item to run.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task that completes when the work item has finished running.</returns>
    ValueTask RunAsync(T workItem, CancellationToken cancellation = default);
}
