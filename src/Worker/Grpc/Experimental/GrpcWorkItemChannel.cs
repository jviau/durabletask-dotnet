// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static Microsoft.DurableTask.Protobuf.TaskHubSidecarService;

#if NETSTANDARD2_0
using Channel = System.Threading.Channels.Channel;
#endif

namespace Microsoft.DurableTask.Worker.Grpc;

/// <summary>
/// Receives gRPC calls and produces work items.
/// </summary>
public partial class GrpcWorkItemChannel : BackgroundService
{
    readonly Channel<WorkItem> channel = Channel.CreateUnbounded<WorkItem>(
        new() { SingleReader = true, SingleWriter = true });

    readonly TaskHubSidecarServiceClient client;
    readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcWorkItemChannel"/> class.
    /// </summary>
    /// <param name="channel">The gRPC channel.</param>
    /// <param name="logger">The logger.</param>
    public GrpcWorkItemChannel(GrpcChannel channel, ILogger<GrpcWorkItemChannel> logger)
    {
        this.client = new(Check.NotNull(channel));
        this.logger = Check.NotNull(logger);
    }

    /// <summary>
    /// Gets the work item channel reader.
    /// </summary>
    public ChannelReader<WorkItem> Reader => this.channel.Reader;

    /// <inheritdoc/>
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Listener listener = new(this.client, this.logger);
        return listener.ExecuteAsync(this.channel.Writer, stoppingToken);
    }
}
