// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using DurableTask.Core;

namespace Microsoft.DurableTask.Worker.OrchestrationServiceShim;

/// <summary>
/// An orchestration channel which shims over <see cref="OrchestrationRuntimeState"/>.
/// </summary>
partial class ShimOrchestrationChannel
{
    class ShimWriter : ChannelWriter<OrchestrationMessage>
    {
        readonly ShimOrchestrationChannel parent;
        bool complete;

        public ShimWriter(ShimOrchestrationChannel parent)
        {
            this.parent = parent;
        }

        public override bool TryWrite(OrchestrationMessage item)
        {
            Check.NotNull(item);
            if (this.complete && this.parent.IsReplaying)
            {
                return false; // ignore while we are replaying.
            }

            this.parent.EnqueueMessage(item);
            return true;
        }

        public override bool TryComplete(Exception? error = null)
        {
            if (this.complete)
            {
                return false;
            }

            this.parent.Abort = error is not null;
            this.complete = true;
            return true;
        }

        public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default)
        {
            return new(!this.complete);
        }
    }
}
