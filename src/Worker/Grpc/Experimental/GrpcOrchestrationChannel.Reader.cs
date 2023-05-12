// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc;

/// <summary>
/// A threading channel for a gRPC orchestrator request.
/// </summary>
partial class GrpcOrchestrationChannel
{
    class GrpcReader : ChannelReader<OrchestrationMessage>
    {
        readonly GrpcOrchestrationChannel parent;

        int next;
        IList<P.HistoryEvent>? events;

        public GrpcReader(GrpcOrchestrationChannel parent)
        {
            this.parent = parent;
            this.events = parent.request.PastEvents;
        }

        public bool IsReplaying => this.events == this.parent.request.PastEvents;

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
            if (this.events == this.parent.request.PastEvents)
            {
                this.next = 0;
                IList<P.HistoryEvent> candidate = this.parent.request.NewEvents;
                if (candidate.Count == 0)
                {
                    // if there are no new events to read, lets just end this event queue now.
                    this.events = null;
                    return false;
                }

                this.events = this.parent.request.NewEvents;
                return true;
            }

            if (this.events == this.parent.request.NewEvents)
            {
                this.events = null;
                this.next = 0;
                return false;
            }

            return false;
        }
    }
}
