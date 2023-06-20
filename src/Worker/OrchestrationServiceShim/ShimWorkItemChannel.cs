// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using DurableTask.Core;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DurableTask.Worker.OrchestrationServiceShim;

/// <summary>
/// Channel for <see cref="IOrchestrationService"/>.
/// </summary>
class ShimWorkItemChannel : BackgroundService
{
    readonly IOrchestrationService service;
    readonly Channel<WorkItem> workQueue = Channel.CreateUnbounded<WorkItem>(
        new() { SingleReader = true, SingleWriter = false });

    /// <summary>
    /// Initializes a new instance of the <see cref="ShimWorkItemChannel"/> class.
    /// </summary>
    /// <param name="service">The orchestration service.</param>
    public ShimWorkItemChannel(IOrchestrationService service)
    {
        this.service = Check.NotNull(service);
    }

    /// <summary>
    /// Gets the work item channel reader.
    /// </summary>
    public ChannelReader<WorkItem> Reader => this.workQueue.Reader;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await this.service.CreateIfNotExistsAsync();
            await this.service.StartAsync();
            await Task.WhenAll(
                this.DequeueActivitiesAsync(stoppingToken),
                this.DequeueOrchestrationsAsync(stoppingToken));
        }
        catch (Exception ex)
        {
            this.workQueue.Writer.TryComplete(ex);
            throw;
        }
        finally
        {
            await this.service.StopAsync();
        }

        this.workQueue.Writer.TryComplete();
    }

    async Task DequeueActivitiesAsync(CancellationToken cancellation)
    {
        while (await this.workQueue.Writer.WaitToWriteAsync(cancellation))
        {
            TaskActivityWorkItem activity = await this.service
                .LockNextTaskActivityWorkItem(Timeout.InfiniteTimeSpan, cancellation);
            if (activity is null)
            {
                continue;
            }

            try
            {
                ShimActivityWorkItem workItem = new(this.service, activity);
                await this.workQueue.Writer.WriteAsync(workItem, cancellation);
            }
            catch
            {
                await this.service.AbandonTaskActivityWorkItemAsync(activity);
            }
        }
    }

    async Task DequeueOrchestrationsAsync(CancellationToken cancellation)
    {
        while (await this.workQueue.Writer.WaitToWriteAsync(cancellation))
        {
            TaskOrchestrationWorkItem orchestration = await this.service.LockNextTaskOrchestrationWorkItemAsync(
                Timeout.InfiniteTimeSpan, cancellation);
            if (orchestration is null)
            {
                continue;
            }

            await this.EnqueueAsync(orchestration, cancellation);
        }
    }

    async ValueTask EnqueueAsync(TaskOrchestrationWorkItem orchestration, CancellationToken cancellation)
    {
        orchestration.PrepareForRun();
        ShimOrchestrationWorkItem workItem = new(this.service, orchestration);

        try
        {
            await this.workQueue.Writer.WriteAsync(workItem, cancellation);
        }
        catch
        {
            await this.service.AbandonTaskOrchestrationWorkItemAsync(orchestration);
            await this.service.ReleaseTaskOrchestrationWorkItemAsync(orchestration);
        }
    }
}
