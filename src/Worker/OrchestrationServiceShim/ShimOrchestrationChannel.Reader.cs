// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using DurableTask.Core;
using DurableTask.Core.History;

namespace Microsoft.DurableTask.Worker.OrchestrationServiceShim;

/// <summary>
/// An orchestration channel which shims over <see cref="OrchestrationRuntimeState"/>.
/// </summary>
partial class ShimOrchestrationChannel
{
    class ShimReader : ChannelReader<OrchestrationMessage>
    {
        readonly ShimOrchestrationChannel parent;

        int next;
        IList<HistoryEvent>? events;
        OrchestrationMessage? explicitMessage;

        public ShimReader(ShimOrchestrationChannel parent)
        {
            this.parent = parent;
            this.events = parent.State.PastEvents;
        }

        public bool IsReplaying => this.events == this.parent.State.PastEvents;

        public override bool TryRead([MaybeNullWhen(false)] out OrchestrationMessage item)
        {
            if (this.explicitMessage is { } message)
            {
                // Explicit messages are used to send messages not part of the runtime state.
                this.explicitMessage = null;
                item = message;
                return true;
            }

            if (this.events is null)
            {
                // we have finished processing both past and new events.
                item = default;
                return false;
            }

            if (this.next >= this.events.Count)
            {
                // move to the next event queue, if there is one.
                if (!this.ProgressEvents())
                {
                    item = default;
                    return false;
                }
            }

            item = ToMessage(this.events[this.next++]);
            if (item is null)
            {
                return this.TryRead(out item);
            }

            return true;
        }

        public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
        {
            async Task<bool> TryRenewSessionAsync(CancellationToken cancellationToken = default)
            {
                if (await this.parent.TryRenewSessionAsync(cancellationToken))
                {
                    this.explicitMessage = new OrchestratorStarted(DateTimeOffset.UtcNow);
                    this.events = this.parent.State.NewEvents;
                    this.next = 0;
                    return true;
                }

                return false;
            }

            return this.events is null ? new(TryRenewSessionAsync(cancellationToken)) : new(true);
        }

        bool ProgressEvents()
        {
            if (this.events == this.parent.State.PastEvents)
            {
                this.next = 0;
                IList<HistoryEvent> candidate = this.parent.State.NewEvents;
                if (candidate.Count == 0)
                {
                    // if there are no new events to read, lets just end this event queue now.
                    this.events = null;
                    return false;
                }

                this.events = this.parent.State.NewEvents;
                return true;
            }

            if (this.events == this.parent.State.NewEvents)
            {
                this.events = null;
                this.next = 0;
                return false;
            }

            return false;
        }
    }
}
