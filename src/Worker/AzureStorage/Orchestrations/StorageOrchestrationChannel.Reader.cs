// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// Storage channel for <see cref="OrchestrationMessage"/>.
/// </summary>
partial class StorageOrchestrationChannel
{
    /**
     * This reader is responsible for streaming in both persisted history (replay) and new messages coming in.
     * First, this will translate the async pageable representing the history into a channel, yielding messages from
     * that. Once that has exhausted its messages, we begin listening to new messages from the queue. Whenever a message
     * is read from this channel it is buffered for 'consumption' (aka: deleting it from the queue). Consumption buffer
     * will be drained under two circumstances: manually when the work item is released, or after it is full and the
     * next WaitToReadAsync() is called. A full consumption buffer will prevent more items from being read, thus forcing
     * a consumption flush.
     *
     * This is written for only a single reader and is not thread safe.
     */
    class StorageReader : ChannelReader<OrchestrationMessage>
    {
        readonly StorageOrchestrationChannel parent;

        ChannelReader<OrchestrationMessage>? history;

        public StorageReader(StorageOrchestrationChannel parent)
        {
            this.parent = parent;
            this.history = new PageableChannelReader<OrchestrationMessage>(this.parent.session.GetHistoryAsync());
        }

        public override Task Completion => this.parent.session.Completion;

        public bool IsReplaying => this.history is not null;

        public override bool TryRead([MaybeNullWhen(false)] out OrchestrationMessage item)
        {
            if (this.Completion.IsCompleted)
            {
                item = null;
                return false;
            }

            if (this.history is not null)
            {
                return this.history.TryRead(out item);
            }

            if (this.parent.FlushNeeded)
            {
                // This will always be true during a flush.
                item = null;
                return false;
            }

            if (this.parent.session.NewMessageReader.TryRead(out WorkDispatch? dispatch))
            {
                this.parent.pendingActions.Writer.TryWrite(() => this.parent.session.ConsumeMessageAsync(dispatch));
                item = dispatch.Message;
                return true;
            }

            item = null;
            return false;
        }

        public override async ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
        {
            if (this.Completion.IsCompleted)
            {
                return false;
            }

            if (await this.ReadHistoryAsync(cancellationToken))
            {
                return true;
            }

            // We will always flush on any WaitToReadAsync call.
            await this.parent.FlushAsync(cancellationToken);
            this.parent.logger.LogDebug("Waiting for new messages.");
            return await this.parent.session.NewMessageReader.WaitToReadAsync(cancellationToken);
        }

        ValueTask<bool> ReadHistoryAsync(CancellationToken cancellation)
        {
            async ValueTask<bool> ReadSlowAsync(CancellationToken cancellation)
            {
                this.parent.logger.LogDebug("Waiting for history to populate.");
                if (await this.history.WaitToReadAsync(cancellation))
                {
                    return true;
                }

                this.parent.logger.LogDebug("Done reading history.");
                if (this.history is IAsyncDisposable disposable)
                {
                    await disposable.DisposeAsync();
                }

                this.history = null;
                return false;
            }

            if (this.history is null)
            {
                return new(false);
            }

            return ReadSlowAsync(cancellation);
        }
    }
}
