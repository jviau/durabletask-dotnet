// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using Microsoft.DurableTask.Worker.AzureStorage.Implementation;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// Channel for providing work items from Azure Storage.
/// </summary>
class WorkItemChannel : BackgroundService
{
    readonly IWorkItemSource activities;
    readonly IWorkItemSource orchestrations;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkItemChannel"/> class.
    /// </summary>
    /// <param name="activities">The activity source.</param>
    /// <param name="orchestrations">The orchestration source.</param>
    public WorkItemChannel(IWorkItemSource activities, IWorkItemSource orchestrations)
    {
        this.activities = Check.NotNull(activities);
        this.orchestrations = Check.NotNull(orchestrations);
        this.Reader = FanInChannelReader.Create(activities.Reader, orchestrations.Reader);
    }

    /// <summary>
    /// Gets the channel reader.
    /// </summary>
    public ChannelReader<WorkItem> Reader { get; }

    /// <inheritdoc/>
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.WhenAll(this.activities.RunAsync(stoppingToken), this.orchestrations.RunAsync(stoppingToken));
    }
}
