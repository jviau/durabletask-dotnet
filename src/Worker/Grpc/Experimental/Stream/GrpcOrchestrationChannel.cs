// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using Grpc.Core;
using static Microsoft.DurableTask.Protobuf.Experimental.DurableTaskHub;
using P = Microsoft.DurableTask.Protobuf.Experimental;

namespace Microsoft.DurableTask.Worker.Grpc.Stream;

/// <summary>
/// A threading channel for a gRPC orchestrator request.
/// </summary>
partial class GrpcOrchestrationChannel : Channel<OrchestrationMessage>, IAsyncDisposable
{
    readonly string instanceId;
    readonly Func<Task> complete;

    bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcOrchestrationChannel"/> class.
    /// </summary>
    /// <param name="instanceId">The orchestration instance ID.</param>
    /// <param name="client">The client.</param>
    public GrpcOrchestrationChannel(string instanceId, DurableTaskHubClient client)
    {
        this.instanceId = Check.NotNullOrEmpty(instanceId);
        AsyncDuplexStreamingCall<P.OrchestratorAction, P.OrchestratorMessage> stream = client.OrchestrationStream();

        this.complete = stream.RequestStream.CompleteAsync;
        this.Reader = new GrpcReader(stream.ResponseStream, this.InitializeAsync);
        this.Writer = new GrpcWriter(stream.RequestStream, _ => { });
    }

    /// <summary>
    /// Gets a value indicating whether this channel is replaying or not.
    /// </summary>
    public bool IsReplaying => ((GrpcReader)this.Reader).IsReplaying;

    /// <summary>
    /// Set the orchestrations custom status.
    /// </summary>
    /// <param name="status">The status to set.</param>
    public void SetStatus(string? status)
    {
        SetSubStatus message = new(status);
        if (!this.Writer.TryWrite(message))
        {
            ValueTask result = this.Writer.WriteAsync(message);
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

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (!this.disposed)
        {
            this.disposed = true;

            // The order of disposable is important:
            // 1. Drain pending writes.
            // 2. Complete gRPC stream.
            // 3. Ensure reader cleanup finishes.
            await ((GrpcWriter)this.Writer).DisposeAsync();
            await this.complete.Invoke();
            await ((GrpcReader)this.Reader).DisposeAsync();
        }
    }

    // This will be called on the first WaitToReadAsync the runner performs. It will kick-start the gRPC stream.
    ValueTask InitializeAsync() => this.Writer.WriteAsync(new InitializeStream(this.instanceId));

    record SetSubStatus(string? Status) : OrchestrationMessage(-1, DateTimeOffset.UtcNow);

    record InitializeStream(string InstanceId) : OrchestrationMessage(-1, DateTimeOffset.UtcNow);
}
