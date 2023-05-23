// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Microsoft.DurableTask.Grpc.Hub;

/// <summary>
/// Implementation from https://github.com/microsoft/vs-threading/blob/main/src/Microsoft.VisualStudio.Threading/AsyncManualResetEvent.cs.
/// </summary>
[DebuggerDisplay("Signaled: {IsSet}")]
class AsyncManualResetEvent
{
    /// <summary>
    /// The object to lock when accessing fields.
    /// </summary>
    readonly object syncObject = new();

    /// <summary>
    /// The source of the task to return from <see cref="WaitAsync(CancellationToken)"/>.
    /// </summary>
    /// <devremarks>
    /// This should not need the volatile modifier because it is
    /// always accessed within a lock.
    /// </devremarks>
    TaskCompletionSource taskCompletionSource;

    /// <summary>
    /// A flag indicating whether the event is signaled.
    /// When this is set to true, it's possible that
    /// <see cref="taskCompletionSource"/>.Task.IsCompleted is still false
    /// if the completion has been scheduled asynchronously.
    /// Thus, this field should be the definitive answer as to whether
    /// the event is signaled because it is synchronously updated.
    /// </summary>
    /// <devremarks>
    /// This should not need the volatile modifier because it is
    /// always accessed within a lock.
    /// </devremarks>
    bool isSet;

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncManualResetEvent"/> class.
    /// </summary>
    /// <param name="set">A value indicating whether the event should be initially signaled.</param>
    public AsyncManualResetEvent(bool set = false)
    {
        this.taskCompletionSource = CreateTaskSource();
        this.isSet = set;
        if (set)
        {
            this.taskCompletionSource.SetResult();
        }
    }

    /// <summary>
    /// Gets a value indicating whether the event is currently in a signaled state.
    /// </summary>
    public bool IsSet
    {
        get
        {
            lock (this.syncObject)
            {
                return this.isSet;
            }
        }
    }

    /// <summary>
    /// Returns a task that will be completed when this event is set.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the event is set, or cancels with the <paramref name="cancellationToken"/>.</returns>
    public Task WaitAsync(CancellationToken cancellationToken = default)
    {
        lock (this.syncObject)
        {
            return this.taskCompletionSource.Task.WaitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Sets this event to unblock callers of <see cref="WaitAsync(CancellationToken)"/>.
    /// </summary>
    public void Set()
    {
        TaskCompletionSource? tcs = null;
        bool transitionRequired = false;
        lock (this.syncObject)
        {
            transitionRequired = !this.isSet;
            tcs = this.taskCompletionSource;
            this.isSet = true;
        }

        if (transitionRequired)
        {
            tcs.TrySetResult();
        }
    }

    /// <summary>
    /// Resets this event to a state that will block callers of <see cref="WaitAsync(CancellationToken)"/>.
    /// </summary>
    public void Reset()
    {
        lock (this.syncObject)
        {
            if (this.isSet)
            {
                this.taskCompletionSource = CreateTaskSource();
                this.isSet = false;
            }
        }
    }

    /// <summary>
    /// Sets and immediately resets this event, allowing all current waiters to unblock.
    /// </summary>
    public void PulseAll()
    {
        TaskCompletionSource? tcs = null;
        lock (this.syncObject)
        {
            // Atomically replace the completion source with a new, uncompleted source
            // while capturing the previous one so we can complete it.
            // This ensures that we don't leave a gap in time where WaitAsync() will
            // continue to return completed Tasks due to a Pulse method which should
            // execute instantaneously.
            tcs = this.taskCompletionSource;
            this.taskCompletionSource = CreateTaskSource();
            this.isSet = false;
        }

        tcs.TrySetResult();
    }

    /// <summary>
    /// Gets an awaiter that completes when this event is signaled.
    /// </summary>
    /// <returns>Gets the awaiter for this event. See <see cref="WaitAsync(CancellationToken)" />.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public TaskAwaiter GetAwaiter()
    {
        return this.WaitAsync().GetAwaiter();
    }

    /// <summary>
    /// Creates a new TaskCompletionSource to represent an unset event.
    /// </summary>
    static TaskCompletionSource CreateTaskSource()
    {
        return new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
