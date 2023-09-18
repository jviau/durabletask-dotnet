// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// A source of work items.
/// </summary>
interface IWorkItemSource
{
    /// <summary>
    /// Gets the work item reader.
    /// </summary>
    ChannelReader<WorkItem> Reader { get; }

    /// <summary>
    /// Runs this source.
    /// </summary>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A task that completes when the work item completes.</returns>
    Task RunAsync(CancellationToken cancellation = default);
}
