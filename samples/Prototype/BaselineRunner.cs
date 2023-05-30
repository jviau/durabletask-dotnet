// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using DurableTask.DependencyInjection;
using DurableTask.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Prototype;

class BaselineRunner : Runner
{
    TaskHubClient client = null!;
    IOrchestrationServicePurgeClient purgeClient = null!;
    readonly BaselineOptions options;

    public BaselineRunner(BaselineOptions options)
    {
        this.options = options;
    }

    protected override string Description => "baseline (DurableTask.Core)";

    protected override Task<IHost> CreateHostAsync()
    {
        Console.WriteLine("Running baseline benchmark");
        IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IOrchestrationService>(sp =>
                {
                    ILoggerFactory lf = sp.GetRequiredService<ILoggerFactory>();
                    return OrchestrationService.CreateAzureStorage("baseline", lf);
                });

                services.AddSingleton(
                    sp => (IOrchestrationServiceClient)sp.GetRequiredService<IOrchestrationService>());
            })
            .ConfigureTaskHubWorker(builder =>
            {
                builder.AddActivity<TestCoreActivity>();
                builder.AddOrchestration<TestCoreOrchestration>();
                builder.AddClient();
            },
            opt => opt.CreateIfNotExists = true)
            .Build();

        this.client = host.Services.GetRequiredService<TaskHubClient>();
        this.purgeClient = (IOrchestrationServicePurgeClient)host.Services
            .GetRequiredService<IOrchestrationServiceClient>();

        return Task.FromResult(host);
    }

    protected override ValueTask DisposeCoreAsync() => default;

    protected override Task IterationCleanupAsync(CancellationToken cancellation)
    {
        OrchestrationStatus[] statuses = new[]
        {
            OrchestrationStatus.Completed,
            OrchestrationStatus.Failed,
            OrchestrationStatus.Pending,
            OrchestrationStatus.Suspended,
            OrchestrationStatus.Running,
            OrchestrationStatus.Canceled,
            OrchestrationStatus.ContinuedAsNew,
            OrchestrationStatus.Terminated,
        };

        PurgeInstanceFilter filter = new(createdTimeFrom: DateTime.MinValue, null, statuses);
        return this.purgeClient.PurgeInstanceStateAsync(filter);
    }

    protected override async Task RunIterationAsync(bool isWarmup, CancellationToken cancellation)
    {
        int depth = isWarmup ? 1 : this.options.Depth;
        async Task RunOrchestrationAsync(int depth)
        {
            await Task.Yield();
            OrchestrationInstance instance = await this.client.CreateOrchestrationInstanceAsync(
                typeof(TestCoreOrchestration), new TestInput(depth, "test-value"));
            OrchestrationState state = await this.client.WaitForOrchestrationAsync(
                instance, TimeSpan.MaxValue, cancellation);
            if (state.OrchestrationStatus != OrchestrationStatus.Completed)
            {
                throw new InvalidOperationException($"Unexpected status of {state.OrchestrationStatus}.");
            }
        }

        Task[] tasks = new Task[this.options.Count];
        for (int i = 0; i < this.options.Count; i++)
        {
            tasks[i] = RunOrchestrationAsync(depth);
        }

        await Task.WhenAll(tasks).WaitAsync(cancellation);
    }
}
