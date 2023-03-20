// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Extensions;

/// <summary>
/// An eternal orchestration, which always continues as new after each iteration.
/// </summary>
/// <typeparam name="TInput">The orchestrator input.</typeparam>
public abstract class EternalOrchestrator<TInput> : TaskOrchestrator<TInput>
{
    /// <summary>
    /// Gets a value indicating whether unprocessed events should be preserved between iterations.
    /// </summary>
    protected virtual bool PreserveUnprocessedEvents => true;

    /// <inheritdoc/>
    public async override Task RunAsync(TaskOrchestrationContext context, TInput input)
    {
        Check.NotNull(context);
        input = await this.RunIterationAsync(context, input);
        context.ContinueAsNew(input, this.PreserveUnprocessedEvents);
    }

    /// <summary>
    /// Runs a single iteration of this eternal orchestration. When this completes,
    /// <see cref="TaskOrchestrationContext.ContinueAsNew(object?, bool)" /> will be performed.
    /// </summary>
    /// <param name="context">The task orchestration context.</param>
    /// <param name="input">The orchestration input.</param>
    /// <returns>The input for the next iteration.</returns>
    protected abstract Task<TInput> RunIterationAsync(TaskOrchestrationContext context, TInput input);
}

/// <summary>
/// An eternal orchestration, which always continues as new after each iteration.
/// </summary>
public abstract class EternalOrchestrator : TaskOrchestrator
{
    /// <summary>
    /// Gets a value indicating whether unprocessed events should be preserved between iterations.
    /// </summary>
    protected virtual bool PreserveUnprocessedEvents => true;

    /// <inheritdoc/>
    public async override Task RunAsync(TaskOrchestrationContext context)
    {
        Check.NotNull(context);
        await this.RunIterationAsync(context);
        context.ContinueAsNew(Unit.Value, this.PreserveUnprocessedEvents);
    }

    /// <summary>
    /// Runs a single iteration of this eternal orchestration. When this completes,
    /// <see cref="TaskOrchestrationContext.ContinueAsNew(object?, bool)" /> will be performed.
    /// </summary>
    /// <param name="context">The task orchestration context.</param>
    /// <returns>A task that completes when this iteration is complete.</returns>
    protected abstract Task RunIterationAsync(TaskOrchestrationContext context);
}
