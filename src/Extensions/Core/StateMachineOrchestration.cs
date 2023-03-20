// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Extensions;

/// <summary>
/// A state-machine event.
/// </summary>
public interface IStateMachineEvent
{
    /// <summary>
    /// Reduces this event with a new incoming event, producing a third event.
    /// </summary>
    /// <param name="incoming">The incoming event.</param>
    /// <param name="onDeck">
    /// The "on deck" event, which is the event that is current scheduled to be processed next.
    /// </param>
    /// <returns>The new "on-deck" event.</returns>
    IStateMachineEvent? ReduceEvent(IStateMachineEvent incoming, IStateMachineEvent? onDeck);

    /// <summary>
    /// Runs this events logic.
    /// </summary>
    /// <param name="context">The task orchestration context.</param>
    /// <returns>A task that completes when this event is done.</returns>
    Task RunAsync(TaskOrchestrationContext context);

    /// <summary>
    /// Finalize this event. This is called only after even collection has ended for this iteration.
    /// </summary>
    /// <param name="context">The task orchestration context.</param>
    /// <returns>A task that completes when finalization is complete.</returns>
    Task FinalizeAsync(TaskOrchestrationContext context);
}

/// <summary>
/// A state-machine orchestration.
/// </summary>
public class StateMachineOrchestration : TaskOrchestrator<IStateMachineEvent>
{
    /// <inheritdoc/>
    public override async Task RunAsync(TaskOrchestrationContext context, IStateMachineEvent input)
    {
        using CancellationTokenSource cts = new();
        Task<IStateMachineEvent?> collect = CollectEventsAsync(context, input, cts.Token);
        await input.RunAsync(context);
        cts.Cancel();
        IStateMachineEvent? next = await collect;
        await input.FinalizeAsync(context);
        if (next is not null)
        {
            context.ContinueAsNew(next);
        }
    }

    static async Task<IStateMachineEvent?> CollectEventsAsync(
        TaskOrchestrationContext context, IStateMachineEvent current, CancellationToken cancellation)
    {
        IStateMachineEvent? onDeck = null;
        try
        {
            while (true)
            {
                IStateMachineEvent incoming = await context.WaitForExternalEvent<IStateMachineEvent>(
                    "next", cancellation);
                onDeck = current.ReduceEvent(incoming, onDeck);
            }
        }
        catch (OperationCanceledException)
        {
        }

        return onDeck;
    }
}
