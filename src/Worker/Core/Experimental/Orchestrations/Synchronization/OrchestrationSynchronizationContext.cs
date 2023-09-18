// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Synchronization context used for orchestration executions.
/// </summary>
class OrchestrationSynchronizationContext : SynchronizationContext, IDisposable
{
    readonly SynchronizationContext previous = Current;
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

    /// <summary>
    /// Enters this sync context.
    /// </summary>
    /// <returns>A disposable to exit the context.</returns>
    public Disposable Enter() => new(this);

    /// <summary>
    /// Suppresses the current sync context.
    /// </summary>
    /// <returns>A disposable to re-enter the context.</returns>
    public Disposable Suppress() => new(this.previous);

    /// <summary>
    /// Sync context disposable.
    /// </summary>
    public readonly struct Disposable : IDisposable
    {
        readonly SynchronizationContext previous;

        /// <summary>
        /// Initializes a new instance of the <see cref="Disposable"/> struct.
        /// </summary>
        /// <param name="context">The sync context to enter.</param>
        public Disposable(SynchronizationContext context)
        {
            this.previous = Current;
            SetSynchronizationContext(context);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            SetSynchronizationContext(this.previous);
        }
    }
}
