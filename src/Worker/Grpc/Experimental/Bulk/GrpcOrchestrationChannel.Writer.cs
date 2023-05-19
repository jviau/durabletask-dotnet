// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;

namespace Microsoft.DurableTask.Worker.Grpc.Bulk;

/// <summary>
/// A threading channel for a gRPC orchestrator request.
/// </summary>
partial class GrpcOrchestrationChannel
{
    class GrpcWriter : ChannelWriter<OrchestrationMessage>
    {
        readonly GrpcOrchestrationChannel parent;

        bool complete;

        public GrpcWriter(GrpcOrchestrationChannel parent)
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

            this.parent.EnqueueAction(item);
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
