// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Synchronization context used for orchestration executions.
/// </summary>
class OrchestrationSynchronizationContext : SynchronizationContext
{
    readonly TaskScheduler scheduler = new SynchronousTaskScheduler();

    /// <inheritdoc/>
    public override void Post(SendOrPostCallback sendOrPostCallback, object state)
    {
        Task.Factory.StartNew(
            s => sendOrPostCallback(s),
            state,
            CancellationToken.None,
            TaskCreationOptions.None,
            this.scheduler);
    }

    /// <inheritdoc/>
    public override void Send(SendOrPostCallback sendOrPostCallback, object state)
    {
        var t = new Task(s => sendOrPostCallback(s), state);
        t.RunSynchronously(this.scheduler);
        t.Wait();
    }
}
