// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Microsoft.DurableTask.Worker.Hosting;
using Microsoft.DurableTask.Worker.OrchestrationServiceShim;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Extensions for configuring OrchestrationServiceShim on <see cref="IDurableTaskWorkerBuilder"/>.
/// </summary>
public static class DurableTaskWorkerBuilderExtensions
{
    /// <summary>
    /// Configures the builder to use the <see cref="ChannelDurableTaskWorker"/>. This requires a
    /// <see cref="IOrchestrationService"/> to be registered in the DI container.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <returns>The same builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseOrchestrationService(this IDurableTaskWorkerBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));
        builder.UseChannels();

        builder.Services.AddSingleton<ShimWorkItemChannel>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ShimWorkItemChannel>());
        builder.Services.AddOptions<ChannelDurableTaskWorkerOptions>(builder.Name)
            .Configure<ShimWorkItemChannel>((o, c) => o.WorkItemReader = c.Reader);
        return builder;
    }
}
