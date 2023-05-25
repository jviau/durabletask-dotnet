// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static Microsoft.DurableTask.Protobuf.Experimental.DurableTaskHub;

namespace Microsoft.DurableTask.Worker.Grpc.Stream;

/// <summary>
/// Receives gRPC calls and produces work items.
/// </summary>
public partial class GrpcWorkItemChannel : BackgroundService
{
    readonly Channel<WorkItem> channel = System.Threading.Channels.Channel.CreateBounded<WorkItem>(
        new BoundedChannelOptions(100) { SingleReader = true, SingleWriter = true });

    readonly DurableTaskHubClient client;
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
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            Listener listener = new(this.client, this.logger);
            await listener.ExecuteAsync(this.channel.Writer, stoppingToken);
        }
        catch (Exception ex)
        {
            this.channel.Writer.TryComplete(ex);
            throw;
        }

        this.channel.Writer.TryComplete();
    }
}
