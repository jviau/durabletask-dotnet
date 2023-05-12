// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using Microsoft.DurableTask.Worker.Hosting;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Options for the <see cref="ChannelDurableTaskWorker"/>.
/// </summary>
public class ChannelDurableTaskWorkerOptions : DurableTaskWorkerOptions
{
    /// <summary>
    /// Gets or sets the channel to read work items from.
    /// </summary>
    public ChannelReader<WorkItem> WorkItemReader { get; set; } = null!;

    /// <summary>
    /// Gets the runners to use for validating.
    /// </summary>
    internal Dictionary<Type, Type> Runners { get; } = new Dictionary<Type, Type>();
}
