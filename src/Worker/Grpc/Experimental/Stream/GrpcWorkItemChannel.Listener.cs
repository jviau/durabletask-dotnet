// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using static Microsoft.DurableTask.Protobuf.Experimental.DurableTaskHub;
using P = Microsoft.DurableTask.Protobuf.Experimental;

namespace Microsoft.DurableTask.Worker.Grpc.Stream;

/// <summary>
/// Receives gRPC calls and produces work items.
/// </summary>
public partial class GrpcWorkItemChannel
{
    class Listener
    {
        readonly DurableTaskHubClient client;
        readonly ILogger logger;

        public Listener(DurableTaskHubClient client, ILogger logger)
        {
            this.client = client;
            this.logger = logger;
        }

        public async Task ExecuteAsync(ChannelWriter<WorkItem> writer, CancellationToken cancellation)
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    P.GetWorkItemsRequest request = new();
                    using AsyncServerStreamingCall<P.WorkItem> stream = this.client.WorkItemStream(
                        request, cancellationToken: cancellation);
                    await this.ProcessWorkItemsAsync(writer, stream, cancellation);
                }
                catch (Exception) when (cancellation.IsCancellationRequested)
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
                { Activity: { } x } => new GrpcActivityWorkItem(x, this.client),
                { Orchestrator: { } x } => new GrpcOrchestrationWorkItem(x, this.client),
            };
        }
    }
}
