// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Worker.Experimental;
using Microsoft.DurableTask.Worker.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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
    /// <returns>The same builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseChannels(this IDurableTaskWorkerBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<WorkItemRunnerOptions>, ConfigureWorkItemRunnerOptions<WorkItemRunnerOptions>>());
        builder.Services.Configure<ChannelDurableTaskWorkerOptions>(builder.Name, opt =>
        {
            opt.Runners[typeof(OrchestrationWorkItem)] = typeof(OrchestrationRunner);
            opt.Runners[typeof(ActivityWorkItem)] = typeof(ActivityRunner);
        });

        builder.UseBuildTarget<ChannelDurableTaskWorker, ChannelDurableTaskWorkerOptions>();
        return builder;
    }
}
