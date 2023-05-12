// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Contract to run work items.
/// </summary>
interface IWorkItemRunner
{
    /// <summary>
    /// Runs the work item.
    /// </summary>
    /// <param name="workItem">The work item to run.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task that completes when the work item has finished running.</returns>
    ValueTask RunAsync(WorkItem workItem, CancellationToken cancellation = default);
}

/// <summary>
/// Contract to run work items.
/// </summary>
/// <typeparam name="T">The work item to run.</typeparam>
public abstract class WorkItemRunner<T> : WorkItemRunner<T, WorkItemRunnerOptions>
    where T : WorkItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorkItemRunner{T}"/> class.
    /// </summary>
    /// <param name="options">The options for this runner.</param>
    protected WorkItemRunner(WorkItemRunnerOptions options)
        : base(options)
    {
    }
}

/// <summary>
/// The work item runner.
/// </summary>
/// <typeparam name="T">The work item to run.</typeparam>
/// <typeparam name="TOptions">The options type for this work item runner.</typeparam>
public abstract class WorkItemRunner<T, TOptions> : IWorkItemRunner
    where T : WorkItem
    where TOptions : WorkItemRunnerOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorkItemRunner{T, TOptions}"/> class.
    /// </summary>
    /// <param name="options">The options for this runner.</param>
    protected WorkItemRunner(TOptions options)
    {
        this.Options = Check.NotNull(options);
    }

    /// <summary>
    /// Gets the options for this runner.
    /// </summary>
    protected TOptions Options { get; }

    /// <summary>
    /// Gets the <see cref="DataConverter"/>.
    /// </summary>
    protected DataConverter Converter => this.Options.DataConverter;

    /// <summary>
    /// Gets the <see cref="IDurableTaskFactory"/>.
    /// </summary>
    protected IDurableTaskFactory Factory => this.Options.Factory;

    /// <inheritdoc/>
    public ValueTask RunAsync(WorkItem workItem, CancellationToken cancellation = default)
    {
        Check.NotNull(workItem);
        T item = Check.IsType<T>(workItem);
        return this.RunAsync(item, cancellation);
    }

    /// <summary>
    /// Runs the work item.
    /// </summary>
    /// <param name="workItem">The work item to run.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task that completes when the work item has finished running.</returns>
    protected abstract ValueTask RunAsync(T workItem, CancellationToken cancellation = default);
}
