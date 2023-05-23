// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using DurableTask.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DurableTask.Benchmarks.EndToEnd;

public abstract class GrpcBenchmark
{
    protected IWebHost HubHost { get; private set; } = null!;

    protected IHost WorkerHost { get; private set; } = null!;

    protected DurableTaskClient Client { get; private set; } = null!;

    protected IOrchestrationServiceClient ServiceClient { get; private set; } = null!;

    protected CancellationToken ShutdownToken { get; private set; }

    [IterationCleanup]
    public void IterationCleanup()
    {
        IOrchestrationServicePurgeClient purgeClient = (IOrchestrationServicePurgeClient)this.ServiceClient;
        PurgeInstanceFilter filter = new(DateTime.MinValue, null, new[]
        {
            OrchestrationStatus.Pending,
            OrchestrationStatus.Running,
            OrchestrationStatus.Terminated,
            OrchestrationStatus.Suspended,
            OrchestrationStatus.Failed,
            OrchestrationStatus.Completed,
            OrchestrationStatus.ContinuedAsNew,
            OrchestrationStatus.Canceled,
        });

        purgeClient.PurgeInstanceStateAsync(filter).GetAwaiter().GetResult();
    }

    protected async Task SetupAsync(IWebHost hub, IHost worker)
    {
        this.HubHost = hub;
        this.WorkerHost = worker;
        this.Client = this.WorkerHost.Services.GetRequiredService<DurableTaskClient>();
        this.ServiceClient = this.HubHost.Services.GetRequiredService<IOrchestrationServiceClient>();
        this.ShutdownToken = this.WorkerHost.Services.GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStopping;
        IOrchestrationService os = this.HubHost.Services.GetRequiredService<IOrchestrationService>();

        await this.HubHost.StartAsync();
        await this.WorkerHost.StartAsync();
        await os.CreateIfNotExistsAsync();
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await this.WorkerHost.StopAsync();
        await this.HubHost.StopAsync();
        this.WorkerHost.Dispose();
        this.HubHost.Dispose();
    }
}
