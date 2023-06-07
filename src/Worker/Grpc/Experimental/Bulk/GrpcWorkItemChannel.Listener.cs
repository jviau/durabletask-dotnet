// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using static Microsoft.DurableTask.Protobuf.TaskHubSidecarService;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc.Bulk;

/// <summary>
/// Receives gRPC calls and produces work items.
/// </summary>
public partial class GrpcWorkItemChannel
{
    class Listener
    {
        static readonly Google.Protobuf.WellKnownTypes.Empty EmptyMessage = new();

        readonly TaskHubSidecarServiceClient sidecar;
        readonly ILogger logger;

        public Listener(TaskHubSidecarServiceClient sidecar, ILogger logger)
        {
            this.sidecar = sidecar;
            this.logger = logger;
        }

        public async Task ExecuteAsync(ChannelWriter<WorkItem> writer, CancellationToken cancellation)
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    using AsyncServerStreamingCall<P.WorkItem> stream = await this.ConnectAsync(cancellation);
                    await this.ProcessWorkItemsAsync(writer, stream, cancellation);
                }
                catch (RpcException) when (cancellation.IsCancellationRequested)
                {
                    // Worker is shutting down - let the method exit gracefully
                    break;
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
                {
                    // Sidecar is shutting down - retry
                    this.logger.SidecarDisconnected(string.Empty);
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
                {
                    // Sidecar is down - keep retrying
                    this.logger.SidecarUnavailable(string.Empty);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    // Shutting down, lets exit gracefully.
                    break;
                }
                catch (Exception ex)
                {
                    // Unknown failure - retry?
                    this.logger.UnexpectedError(ex, string.Empty);
                }

                try
                {
                    // CONSIDER: Exponential backoff
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellation);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    // Worker is shutting down - let the method exit gracefully
                    break;
                }
            }
        }

        async Task<AsyncServerStreamingCall<P.WorkItem>> ConnectAsync(CancellationToken cancellation)
        {
            await this.sidecar!.HelloAsync(EmptyMessage, cancellationToken: cancellation);
            this.logger.EstablishedWorkItemConnection();

            // Get the stream for receiving work-items
            return this.sidecar!.GetWorkItems(new P.GetWorkItemsRequest(), cancellationToken: cancellation);
        }

        async Task ProcessWorkItemsAsync(
            ChannelWriter<WorkItem> writer, AsyncServerStreamingCall<P.WorkItem> stream, CancellationToken cancellation)
        {
            while (!cancellation.IsCancellationRequested)
            {
                await foreach (P.WorkItem workItem in stream.ResponseStream.ReadAllAsync(cancellation))
                {
                    await writer.WriteAsync(this.ToWorkItem(workItem), cancellation);
                }
            }
        }

        WorkItem ToWorkItem(P.WorkItem workItem)
        {
            return workItem switch
            {
                { ActivityRequest: { } x } => new GrpcActivityWorkItem(x, this.sidecar),
                { OrchestratorRequest: { } x } => new GrpcOrchestrationWorkItem(x, this.sidecar),
            };
        }
    }
}
