// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// Storage channel for <see cref="OrchestrationMessage"/>.
/// </summary>
partial class StorageOrchestrationChannel
{
    class StorageWriter : ChannelWriter<OrchestrationMessage>
    {
        readonly StorageOrchestrationChannel parent;

        public StorageWriter(StorageOrchestrationChannel parent)
        {
            this.parent = parent;
        }

        public override bool TryWrite(OrchestrationMessage item)
        {
            if (this.parent.FlushNeeded)
            {
                return false;
            }

            if (this.parent.IsReplaying)
            {
                // Ignore any writes during a replay.
                return true;
            }

            return this.parent.pendingActions.Writer.TryWrite(() => this.parent.session.SendNewMessageAsync(item));
        }

        public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default)
        {
            async Task<bool> WaitForFlushAsync(CancellationToken cancellation)
            {
                await this.parent.FlushAsync(cancellation);
                return await this.WaitToWriteAsync(cancellation);
            }

            if (this.parent.FlushNeeded)
            {
                return new(WaitForFlushAsync(cancellationToken));
            }

            return this.parent.pendingActions.Writer.WaitToWriteAsync(cancellationToken);
        }
    }
}
