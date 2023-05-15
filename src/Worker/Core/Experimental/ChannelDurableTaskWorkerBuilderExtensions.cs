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

        builder.UseBuildTarget<ChannelDurableTaskWorker, ChannelDurableTaskWorkerOptions>();
        builder.AddRunner<OrchestrationWorkItem, OrchestrationRunner, OrchestrationRunnerOptions>();
        builder.AddRunner<ActivityWorkItem, ActivityRunner, WorkItemRunnerOptions>();
        return builder;
    }

    /// <summary>
    /// Adds a work item runner to the <see cref="ChannelDurableTaskWorker"/>.
    /// </summary>
    /// <typeparam name="TWorkItem">The work item to run.</typeparam>
    /// <typeparam name="TRunner">The runner for the work item.</typeparam>
    /// <typeparam name="TRunnerOptions">The runner options.</typeparam>
    /// <param name="builder">The builder to confingure.</param>
    /// <returns>The same builder, for call chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// If the current build target is not <see cref="ChannelDurableTaskWorker"/>.
    /// </exception>
    public static IDurableTaskWorkerBuilder AddRunner<TWorkItem, TRunner, TRunnerOptions>(
        this IDurableTaskWorkerBuilder builder)
        where TWorkItem : WorkItem
        where TRunner : WorkItemRunner<TWorkItem, TRunnerOptions>
        where TRunnerOptions : WorkItemRunnerOptions
    {
        Check.NotNull(builder, nameof(builder));
        if (builder.BuildTarget != typeof(ChannelDurableTaskWorker))
        {
            throw new InvalidOperationException("Can only be used with ChannelDurableTaskWorker.");
        }

        builder.Services.Configure<ChannelDurableTaskWorkerOptions>(builder.Name, opt =>
        {
            opt.Runners[typeof(TWorkItem)] = typeof(TRunner);
        });

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<TRunnerOptions>, ConfigureWorkItemRunnerOptions<TRunnerOptions>>());

        return builder;
    }
}
