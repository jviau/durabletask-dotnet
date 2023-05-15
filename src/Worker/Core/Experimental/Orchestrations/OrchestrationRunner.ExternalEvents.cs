// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Runs orchestrations.
/// </summary>
partial class OrchestrationRunner
{
    /// <summary>
    /// Event source contract.
    /// </summary>
    interface IEventSource
    {
        /// <summary>
        /// Gets the type of the event stored in the completion source.
        /// </summary>
        Type EventType { get; }

        /// <summary>
        /// Tries to set the result on tcs.
        /// </summary>
        /// <param name="result">The result.</param>
        void TrySetResult(object? result);
    }

    class ExternalEventSource
    {
        readonly NamedQueue<IEventSource> waiters = new();
        readonly NamedQueue<EventReceived> buffer = new();
        readonly DataConverter converter;

        public ExternalEventSource(DataConverter converter)
        {
            this.converter = converter;
        }

        /// <summary>
        /// Waits for an event <paramref name="name"/> to arrive.
        /// </summary>
        /// <typeparam name="T">The type to deserialize the event payload as.</typeparam>
        /// <param name="name">The name of the event to wait on.</param>
        /// <param name="cancellation">Token to abort waiting on the external event.</param>
        /// <returns>The payload of the event.</returns>
        /// <exception cref="InvalidOperationException">
        /// If <typeparamref name="T"/> does not match existing wait handles for <paramref name="name"/>, if there are any.
        /// </exception>
        public Task<T> WaitAsync<T>(string name, CancellationToken cancellation = default)
        {
            if (this.buffer.TryDequeue(name, out EventReceived? message))
            {
                return Task.FromResult(this.converter.Deserialize<T>(message.Input)!);
            }

            EventTaskCompletionSource<T> waiter = new();
            if (this.waiters.TryPeek(name, out IEventSource? existing))
            {
                if (existing.EventType != typeof(T))
                {
                    throw new InvalidOperationException("Events with the same name must have the same type argument." +
                        $"Expected {existing.EventType}, received {typeof(T)}.");
                }
            }

            this.waiters.Enqueue(name, waiter);
            cancellation.Register(() => waiter.TrySetCanceled());
            return waiter.Task;
        }

        /// <summary>
        /// Completes the external event by name, allowing the orchestration to continue if it is waiting on this event.
        /// </summary>
        /// <param name="message">The incoming event message.</param>
        public void OnExternalEvent(EventReceived message)
        {
            // Events are completed in FIFO order.
            if (this.waiters.TryDequeue(message.Name, out IEventSource? waiter))
            {
                object? value = this.converter.Deserialize(message.Input, waiter.EventType);
                waiter.TrySetResult(value);
            }
            else
            {
                // Orchestration is not waiting on this event (yet?). Save it for later consumption.
                this.buffer.Enqueue(message.Name, message);
            }
        }

        public IEnumerable<EventReceived> DrainBuffer() => this.buffer.TakeAll().Select(x => x.Value);
    }

    class EventTaskCompletionSource<T> : TaskCompletionSource<T>, IEventSource
    {
        /// <inheritdoc/>
        public Type EventType => typeof(T);

        /// <inheritdoc/>
        void IEventSource.TrySetResult(object? result)
        {
            if (result is null)
            {
                this.TrySetResult(default!);
            }
            else
            {
                this.TrySetResult((T)result);
            }
        }
    }

    class NamedQueue<TValue>
    {
        readonly Dictionary<string, Queue<TValue>> queues = new(StringComparer.OrdinalIgnoreCase);

        public void Enqueue(string name, TValue value)
        {
            if (!this.queues.TryGetValue(name, out Queue<TValue>? queue))
            {
                queue = new Queue<TValue>();
                this.queues[name] = queue;
            }

            queue.Enqueue(value);
        }

        public bool TryDequeue(string name, [NotNullWhen(true)] out TValue? value)
        {
            if (this.TryGetQueue(name, out Queue<TValue>? queue))
            {
                if (queue.Count < 1)
                {
                    this.queues.Remove(name);
                    value = default;
                    return false;
                }

                value = queue.Dequeue()!;
                if (queue.Count == 0)
                {
                    this.queues.Remove(name);
                }

                return true;
            }

            value = default;
            return false;
        }

        public bool TryPeek(string name, [NotNullWhen(true)] out TValue? value)
        {
            if (this.TryGetQueue(name, out Queue<TValue>? queue))
            {
                value = queue.Peek()!;
                return true;
            }

            value = default;
            return false;
        }

        public IEnumerable<(string Name, TValue Value)> TakeAll()
        {
            foreach ((string name, Queue<TValue> value) in this.queues)
            {
                foreach (TValue payload in value)
                {
                    yield return (name, payload);
                }
            }

            this.queues.Clear();
        }

        bool TryGetQueue(string name, [NotNullWhen(true)] out Queue<TValue>? queue)
        {
            if (this.queues.TryGetValue(name, out queue))
            {
                if (queue.Count == 0)
                {
                    this.queues.Remove(name);
                    queue = null;
                    return false;
                }

                return true;
            }

            return false;
        }
    }
}
