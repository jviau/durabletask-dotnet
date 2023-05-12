// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Worker.Experimental;

/// <summary>
/// Configures <see cref="WorkItemRunnerOptions"/>.
/// </summary>
class ConfigureChannelDurableTaskWorkerOptions : IConfigureNamedOptions<ChannelDurableTaskWorkerOptions>
{
    /// <inheritdoc/>
    public void Configure(string name, ChannelDurableTaskWorkerOptions options)
    {
        Check.NotNull(options);
    }

    /// <inheritdoc/>
    public void Configure(ChannelDurableTaskWorkerOptions options) => this.Configure(Options.DefaultName, options);
}
