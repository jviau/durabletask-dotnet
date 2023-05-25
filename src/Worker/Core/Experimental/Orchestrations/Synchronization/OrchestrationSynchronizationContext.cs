// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Synchronization context used for orchestration executions.
/// </summary>
class OrchestrationSynchronizationContext : SynchronizationContext, IDisposable
{
    readonly CancellationTokenSource cts = new();
    readonly TaskScheduler scheduler = new SynchronousTaskScheduler();

    /// <inheritdoc/>
    public void Dispose() => this.cts.Dispose();

    /// <summary>
    /// Cancel all pending tasks.
    /// </summary>
    public void Cancel() => this.cts.Cancel();

    /// <inheritdoc/>
    public override void Post(SendOrPostCallback sendOrPostCallback, object state)
    {
        Task.Factory.StartNew(
            s => sendOrPostCallback(s),
            state,
            this.cts.Token,
            TaskCreationOptions.None,
            this.scheduler);
    }

    /// <inheritdoc/>
    public override void Send(SendOrPostCallback sendOrPostCallback, object state)
    {
        var t = new Task(s => sendOrPostCallback(s), state, this.cts.Token);
        t.RunSynchronously(this.scheduler);
        t.Wait();
    }
}
