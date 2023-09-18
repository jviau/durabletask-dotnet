// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// Routers messages to active orchestrations.
/// </summary>
class OrchestrationMessageRouter
{
    readonly ConcurrentDictionary<string, Dispatcher> orchestrationChannels = new();
    readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestrationMessageRouter"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public OrchestrationMessageRouter(ILogger logger)
    {
        this.logger = Check.NotNull(logger);
    }

    /// <summary>
    /// Attemps to deliver a message to an activer orchestration.
    /// </summary>
    /// <param name="id">The ID of the orchestration to deliver to.</param>
    /// <param name="message">The message to deliver.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>True if delivered, false otherwise.</returns>
    public ValueTask<bool> DeliverAsync(
        string id, WorkDispatch message, CancellationToken cancellation = default)
    {
        static async Task<bool> SlowAsync(ValueTask task)
        {
            await task;
            return true;
        }

        if (this.orchestrationChannels.TryGetValue(id, out Dispatcher dispatcher))
        {
            this.logger.LogInformation("Delivering new message to orchestration {InstanceId}", id);
            ValueTask task = dispatcher.WriteAsync(message, cancellation);
            if (task.IsCompletedSuccessfully)
            {
                return new(true);
            }

            return new(SlowAsync(task));
        }

        return new(false);
    }

    /// <summary>
    /// Initializers a channel for a given ID.
    /// </summary>
    /// <param name="message">The first message to deliver.</param>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>The reader of the initialized channel.</returns>
    public ValueTask<WorkDispatchReader> InitializeAsync(
        WorkDispatch message, CancellationToken cancellation = default)
    {
        Check.NotNull(message);
        if (this.orchestrationChannels.ContainsKey(message.Id))
        {
            throw new InvalidOperationException($"Key already exists {message.Id}");
        }

        return Dispatcher.CreateAsync(message, this, cancellation);
    }

    class Dispatcher : WorkDispatchReader
    {
        readonly string id;
        readonly Channel<WorkDispatch> channel;
        readonly OrchestrationMessageRouter router;

        Dispatcher(string id, OrchestrationMessageRouter router)
        {
            this.id = id;
            this.router = router;
            this.channel = this.CreateChannel();
        }

        public override bool CanCount => this.Inner.CanCount;

        public override bool CanPeek => this.Inner.CanPeek;

        public override int Count => this.Inner.Count;

        public override Task Completion => this.Inner.Completion;

        ChannelReader<WorkDispatch> Inner => this.channel.Reader;

        public static async ValueTask<WorkDispatchReader> CreateAsync(
            WorkDispatch dispatch, OrchestrationMessageRouter router, CancellationToken cancellation)
        {
            Dispatcher reader = new(dispatch.Id, router);
            await reader.WriteAsync(dispatch, cancellation);
            return reader;
        }

        public override bool TryRead([MaybeNullWhen(false)] out WorkDispatch item)
            => this.Inner.TryRead(out item);

        public override bool TryPeek([MaybeNullWhen(false)] out WorkDispatch item)
            => this.Inner.TryPeek(out item);

        public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
            => this.Inner.WaitToReadAsync(cancellationToken);

        public override ValueTask<WorkDispatch> ReadAsync(CancellationToken cancellationToken = default)
            => this.Inner.ReadAsync(cancellationToken);

        public ValueTask WriteAsync(WorkDispatch dispatch, CancellationToken cancellation)
        {
            return this.channel.Writer.WriteAsync(dispatch, cancellation);
        }

        public override ValueTask DisposeAsync()
        {
            this.router.orchestrationChannels.TryRemove(this.id, out _);
            return default;
        }

        Channel<WorkDispatch> CreateChannel()
        {
            Channel<WorkDispatch> channel = Channel.CreateUnbounded<WorkDispatch>(
                new() { SingleReader = true, SingleWriter = true });
            this.router.orchestrationChannels[this.id] = this;
            return channel;
        }
    }
}
