// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Cron = Cronos.CronExpression;

namespace Microsoft.DurableTask.Extensions;

/// <summary>
/// A timer orchestration, an eternal orchestration which triggers are an interval defined by a CRON expression.
/// </summary>
/// <typeparam name="TInput">The orchestrator input.</typeparam>
public abstract class TimerOrchestrator<TInput> : EternalOrchestrator<TInput>
{
    /// <summary>
    /// Gets the CRON expression for this timer.
    /// </summary>
    protected abstract string CronExpression { get; }

    /// <inheritdoc/>
    public async override Task RunAsync(TaskOrchestrationContext context, TInput input)
    {
        Check.NotNull(context);
        input = await this.RunIterationAsync(context, input);
        if (await TimerHelper.NextAsync(context, this.CronExpression))
        {
            context.ContinueAsNew(input, this.PreserveUnprocessedEvents);
        }
    }
}

/// <summary>
/// A timer orchestration, an eternal orchestration which triggers are an interval defined by a CRON expression.
/// </summary>
public abstract class TimerOrchestrator : EternalOrchestrator
{
    /// <summary>
    /// Gets the CRON expression for this timer.
    /// </summary>
    protected abstract string CronExpression { get; }

    /// <inheritdoc/>
    public async override Task RunAsync(TaskOrchestrationContext context)
    {
        Check.NotNull(context);
        await this.RunIterationAsync(context);
        if (await TimerHelper.NextAsync(context, this.CronExpression))
        {
            context.ContinueAsNew(Unit.Value, this.PreserveUnprocessedEvents);
        }
    }
}

/// <summary>
/// Timer helpers.
/// </summary>
static class TimerHelper
{
    /// <summary>
    /// Delay until the next cron interval, or return right away.
    /// </summary>
    /// <param name="context">The task orchestration context.</param>
    /// <param name="cronExpression">The cron expression.</param>
    /// <returns>True to continue to next iteration, false otherwise.</returns>
    public static async Task<bool> NextAsync(TaskOrchestrationContext context, string cronExpression)
    {
        Cron cron = Cron.Parse(cronExpression);
        DateTime? next = cron.GetNextOccurrence(context.CurrentUtcDateTime);
        if (next is null)
        {
            return false;
        }

        await context.CreateTimer(next.Value, default);
        return true;
    }
}
