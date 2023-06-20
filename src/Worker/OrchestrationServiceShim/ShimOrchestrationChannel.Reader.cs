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

        public ShimReader(ShimOrchestrationChannel parent)
        {
            this.parent = parent;
            this.events = parent.state.PastEvents;
        }

        public bool IsReplaying => this.events == this.parent.state.PastEvents;

        public override bool TryRead([MaybeNullWhen(false)] out OrchestrationMessage item)
        {
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
            return new(this.events is not null);
        }

        bool ProgressEvents()
        {
            if (this.events == this.parent.state.PastEvents)
            {
                this.next = 0;
                IList<HistoryEvent> candidate = this.parent.state.NewEvents;
                if (candidate.Count == 0)
                {
                    // if there are no new events to read, lets just end this event queue now.
                    this.events = null;
                    return false;
                }

                this.events = this.parent.state.NewEvents;
                return true;
            }

            if (this.events == this.parent.state.NewEvents)
            {
                this.events = null;
                this.next = 0;
                return false;
            }

            return false;
        }
    }
}
