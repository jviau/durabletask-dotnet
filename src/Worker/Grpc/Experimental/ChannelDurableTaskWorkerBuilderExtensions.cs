// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Worker.Grpc;
using Microsoft.DurableTask.Worker.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// <see cref="IDurableTaskWorkerBuilder"/> extensions for <see cref="ChannelDurableTaskWorker"/>.
/// </summary>
public static class ChannelDurableTaskWorkerBuilderExtensions
{
    /// <summary>
    /// Configures the builder to use the <see cref="ChannelDurableTaskWorker"/>.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="channel">The gRPC channel to use.</param>
    /// <returns>The same builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseGrpcChannel(
        this IDurableTaskWorkerBuilder builder, GrpcChannel channel)
    {
        Check.NotNull(builder, nameof(builder));
        builder.UseChannels();

        builder.Services.AddSingleton(sp => ActivatorUtilities.CreateInstance<GrpcWorkItemChannel>(sp, channel));
        builder.Services.AddHostedService(sp => sp.GetRequiredService<GrpcWorkItemChannel>());
        builder.Services.AddOptions<ChannelDurableTaskWorkerOptions>(builder.Name)
            .Configure<GrpcWorkItemChannel>((o, c) => o.WorkItemReader = c.Reader);
        return builder;
    }
}
