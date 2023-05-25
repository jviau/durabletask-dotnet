// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Grpc.Core;
using P = Microsoft.DurableTask.Protobuf.Experimental;

namespace Microsoft.DurableTask.Worker.Grpc.Stream;

/// <summary>
/// A threading channel for a gRPC orchestrator request.
/// </summary>
partial class GrpcOrchestrationChannel
{
    class GrpcReader : ChannelReader<OrchestrationMessage>, IAsyncDisposable
    {
        readonly IAsyncStreamReader<P.OrchestratorMessage> stream;
        readonly Channel<P.OrchestratorMessage> readBuffer =
            System.Threading.Channels.Channel.CreateBounded<P.OrchestratorMessage>(
                new BoundedChannelOptions(10)
                {
                    SingleReader = true,
                    SingleWriter = true,
                });

        readonly Func<ValueTask> initialize;
        readonly Task readTask;

        bool initialized;

        public GrpcReader(IAsyncStreamReader<P.OrchestratorMessage> stream, Func<ValueTask> initialize)
        {
            this.stream = Check.NotNull(stream);
            this.initialize = Check.NotNull(initialize);
            this.readTask = Task.Run(this.ReadEventsAsync);
        }

        public bool IsReplaying { get; private set; } = true;

        public ValueTask DisposeAsync()
        {
            this.readBuffer.Writer.TryComplete();
            return new(this.readTask);
        }

        public override bool TryRead([MaybeNullWhen(false)] out OrchestrationMessage item)
        {
            if (!this.readBuffer.Reader.TryRead(out P.OrchestratorMessage? message))
            {
                item = null;
                return false;
            }

            DateTimeOffset timestamp = message.Timestamp.ToDateTimeOffset();
            int id = message.Id;
            switch (message)
            {
                case { Resumed: not null }:
                    this.IsReplaying = false;
                    return this.TryRead(out item);
                case { Disconnect: not null }:
                    this.readBuffer.Writer.TryComplete();
                    item = null;
                    return false;
                case { Started: { } m }:
                    item = new ExecutionStarted(timestamp, m.Input);
                    return true;
                case { Completed: { } m }:
                    item = new ExecutionCompleted(id, timestamp, m.Result, m.Error?.Convert());
                    return true;
                case { Terminated: { } m }:
                    item = new ExecutionTerminated(id, timestamp, m.Reason);
                    return true;
                case { Continued: { } m }:
                    item = new ContinueAsNew(id, timestamp, m.Input, m.Version);
                    return true;
                case { TaskScheduled: { } m }:
                    item = new TaskActivityScheduled(id, timestamp, m.Name.Convert(), m.Input);
                    return true;
                case { TaskCompleted: { } m }:
                    item = new TaskActivityCompleted(id, timestamp, m.ScheduledId, m.Result, m.Error?.Convert());
                    return true;
                case { OrchestrationScheduled: { } m }:
                    item = new SubOrchestrationScheduled(id, timestamp, m.Name.Convert(), m.Input, null);
                    return true;
                case { OrchestrationCompleted: { } m }:
                    item = new SubOrchestrationCompleted(id, timestamp, m.ScheduledId, m.Result, m.Error?.Convert());
                    return true;
                case { TimerCreated: { } m }:
                    item = new TimerScheduled(id, timestamp, m.FireAt.ToDateTimeOffset());
                    return true;
                case { TimerFired: { } m }:
                    item = new TimerFired(id, timestamp, m.ScheduledId);
                    return true;
                case { EventRaised: { } m }:
                    item = new EventReceived(id, timestamp, m.Name, m.Input);
                    return true;
                case { EventSent: { } m }:
                    item = new EventSent(id, timestamp, m.InstanceId, m.Name, m.Input);
                    return true;
                case { Generic: { } m }:
                    item = new GenericMessage(id, timestamp, m.Name, m.Data);
                    return true;
                default:
                    return this.TryRead(out item); // unknown event, move on to next one.
            }
        }

        public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
        {
            async Task<bool> InitializeAsync(CancellationToken cancellation)
            {
                await this.initialize.Invoke();
                return await this.readBuffer.Reader.WaitToReadAsync(cancellation);
            }

            if (!this.initialized)
            {
                this.initialized = true;
                return new(InitializeAsync(cancellationToken));
            }

            return this.readBuffer.Reader.WaitToReadAsync(cancellationToken);
        }

        async Task ReadEventsAsync()
        {
            try
            {
                await foreach (P.OrchestratorMessage? item in this.stream.ReadAllAsync(default))
                {
                    if (item is null)
                    {
                        continue;
                    }

                    await this.readBuffer.Writer.WriteAsync(item);
                }
            }
            catch (Exception ex)
            {
                this.readBuffer.Writer.TryComplete(ex);
                throw;
            }
        }
    }
}
