// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Worker.Experimental;

/// <summary>
/// Configures <see cref="WorkItemRunnerOptions"/>.
/// </summary>
/// <typeparam name="TOptions">The options type to configure.</typeparam>
class ConfigureWorkItemRunnerOptions<TOptions> : IConfigureNamedOptions<TOptions>
    where TOptions : WorkItemRunnerOptions
{
    readonly IOptionsMonitor<ChannelDurableTaskWorkerOptions> monitor;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigureWorkItemRunnerOptions{TOptions}"/> class.
    /// </summary>
    /// <param name="monitor">Options monitor.</param>
    public ConfigureWorkItemRunnerOptions(IOptionsMonitor<ChannelDurableTaskWorkerOptions> monitor)
    {
        this.monitor = Check.NotNull(monitor);
    }

    /// <inheritdoc/>
    public void Configure(string name, TOptions options)
    {
        Check.NotNull(options);
        ChannelDurableTaskWorkerOptions source = this.monitor.Get(name);
        options.DataConverter = source.DataConverter;

        if (options is OrchestrationRunnerOptions o)
        {
            // TODO: move this?
            o.MaximumTimerInterval = source.MaximumTimerInterval;
        }
    }

    /// <inheritdoc/>
    public void Configure(TOptions options) => this.Configure(Options.DefaultName, options);
}
