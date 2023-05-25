// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using DurableTask.Core;

namespace Microsoft.DurableTask.Benchmarks.EndToEnd;

public abstract class CoreBenchmark
{
    readonly CancellationTokenSource cts = new();
    IOrchestrationService orchestrationService = null!;
    TaskHubWorker worker = null!;

    protected TaskHubClient Client { get; private set; } = null!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        this.orchestrationService = OrchestrationService.CreateAzureStorage("dtfxcore");
        this.worker = new(this.orchestrationService);
        this.worker.AddTaskOrchestrations(typeof(TestCoreOrchestration));
        this.worker.AddTaskActivities(typeof(TestCoreActivity));

        await this.worker.StartAsync();

        Console.CancelKeyPress += this.CancelKeyPress;
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        IOrchestrationServicePurgeClient purgeClient = (IOrchestrationServicePurgeClient)this.orchestrationService;

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
        purgeClient.PurgeInstanceStateAsync(filter).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        Console.CancelKeyPress -= this.CancelKeyPress;
        this.cts.Dispose();
        TimeSpan timeout = TimeSpan.FromSeconds(5);
        Task delay =Task.Delay(timeout);
        if (await Task.WhenAny(this.worker.StopAsync(), delay) == delay)
        {
            await this.worker.StopAsync(true);
        }
    }

    protected async Task RunScaleAsync(int count, int depth)
    {
        async Task RunOrchestrationAsync()
        {
            await Task.Yield();
            OrchestrationInstance instance = await this.Client.CreateOrchestrationInstanceAsync(
                typeof(TestCoreOrchestration), new TestInput(depth, "test-value"));
            OrchestrationState state = await this.Client.WaitForOrchestrationAsync(
                instance, TimeSpan.MaxValue, this.cts.Token);
            if (state.OrchestrationStatus != OrchestrationStatus.Completed)
            {
                throw new InvalidOperationException($"Unexpected status of {state.OrchestrationStatus}.");
            }
        }

        Task[] tasks = new Task[count];
        for (int i = 0; i < count; i++)
        {
            tasks[i] = RunOrchestrationAsync();
        }

        await Task.WhenAll(tasks).WaitAsync(this.cts.Token);
    }

    void CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        if (!this.cts.IsCancellationRequested)
        {
            this.cts.Cancel();
        }
    }
}
