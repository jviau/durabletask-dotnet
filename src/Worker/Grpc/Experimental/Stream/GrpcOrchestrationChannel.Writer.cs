// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using Grpc.Core;
using P = Microsoft.DurableTask.Protobuf.Experimental;

namespace Microsoft.DurableTask.Worker.Grpc.Stream;

/// <summary>
/// A threading channel for a gRPC orchestrator request.
/// </summary>
partial class GrpcOrchestrationChannel
{
    class GrpcWriter : ChannelWriter<OrchestrationMessage>, IAsyncDisposable
    {
        readonly IAsyncStreamWriter<P.OrchestratorAction> stream;
        readonly Channel<OrchestrationMessage> exportBuffer =
            System.Threading.Channels.Channel.CreateBounded<OrchestrationMessage>(
                new BoundedChannelOptions(10)
                {
                    SingleReader = true,
                    SingleWriter = true,
                });

        readonly Task exportTask;
        readonly Action<Exception?> onComplete;

        public GrpcWriter(IAsyncStreamWriter<P.OrchestratorAction> stream, Action<Exception?> onComplete)
        {
            this.stream = stream;
            this.exportTask = Task.Run(this.FlushBufferAsync);
            this.onComplete = onComplete;
        }

        public void SetStatus(string? status)
        {
            SetSubStatus message = new(status);
            if (!this.TryWrite(message))
            {
                ValueTask result = this.WriteAsync(message);
                if (result.IsCompleted)
                {
                    result.GetAwaiter().GetResult();
                }
                else
                {
                    result.AsTask().GetAwaiter().GetResult();
                }
            }
        }

        public override bool TryWrite(OrchestrationMessage item)
        {
            Check.NotNull(item);
            return this.exportBuffer.Writer.TryWrite(item);
        }

        public override bool TryComplete(Exception? error = null)
        {
            bool completed = this.exportBuffer.Writer.TryComplete(error);
            if (completed)
            {
                this.onComplete(error);
            }

            return completed;
        }

        public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default)
        {
            return this.exportBuffer.Writer.WaitToWriteAsync(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            this.TryComplete();
            return new(this.exportTask);
        }

        async Task FlushBufferAsync()
        {
            // ReadAllAsync is not available in netstandard 2.0
            while (await this.exportBuffer.Reader.WaitToReadAsync())
            {
                while (this.exportBuffer.Reader.TryRead(out OrchestrationMessage? message))
                {
                    P.OrchestratorAction action = message switch
                    {
                        SetSubStatus s => new() { Id = -1, SetStatus = new() { Status = s.Status } },
                        InitializeStream s => new() { Id = -1, Start = new() { InstanceId = s.InstanceId } },
                        _ => message.ToGrpcAction(),
                    };

                    await this.stream.WriteAsync(action);
                }
            }
        }
    }
}
