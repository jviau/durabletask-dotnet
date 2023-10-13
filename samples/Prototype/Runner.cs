// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DurableTask.Prototype;

abstract class Runner : IAsyncDisposable
{
    IHost? host;

    protected abstract string Description { get; }

    public async Task RunAsync()
    {
        this.host = await this.CreateHostAsync();
        IHostApplicationLifetime lifetime = this.host.Services.GetRequiredService<IHostApplicationLifetime>();
        CancellationToken cancellation = lifetime.ApplicationStopping;
        await this.host.StartAsync();

        await this.IterationCleanupAsync(cancellation);

        // warmup
        for (int i = 0; i < 5; i++)
        {
            await this.RunIterationAsync(true, cancellation);
            Console.WriteLine($"Warming up {i + 1}/5");
            await this.IterationCleanupAsync(cancellation);
        }

        TimeSpan[] times = new TimeSpan[10];
        for (int i = 0; i < 10; i++)
        {
            times[i] = await this.TimedIterationAsync(cancellation);
            Console.WriteLine($"Itreration {i + 1}/10 -- {times[i].TotalMilliseconds}");
            await this.IterationCleanupAsync(cancellation);
        }

        var ms = times.Select(t => t.TotalMilliseconds).ToList();
        double avg = ms.Average();
        double stdDev = Math.Sqrt(ms.Average(v => Math.Pow(v - avg, 2)));
        Console.WriteLine($"{this.Description} -- Average: {avg}. StdDev: {stdDev}");
    }

    public ValueTask DisposeAsync()
    {
        this.host?.Dispose();
        return this.DisposeCoreAsync();
    }

    protected abstract Task<IHost> CreateHostAsync();
    protected abstract Task RunIterationAsync(bool isWarmup, CancellationToken cancellation);
    protected abstract Task IterationCleanupAsync(CancellationToken cancellation);
    protected abstract ValueTask DisposeCoreAsync();

     async Task<TimeSpan> TimedIterationAsync(CancellationToken cancellation)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        await this.RunIterationAsync(false, cancellation);
        stopwatch.Stop();
        return stopwatch.Elapsed;
    }
}

abstract class PrototypeRunner<TOptions> : Runner
    where TOptions : StartupOptions
{
    DurableTaskClient client = null!;

    public PrototypeRunner(TOptions options)
    {
        this.Options = options;
    }

    protected TOptions Options { get; }

    protected override string Description => this.Options.Description;

    protected override async Task<IHost> CreateHostAsync()
    {
        IHost host = await this.CreateHostCoreAsync();
        this.client = host.Services.GetRequiredService<DurableTaskClient>();
        return host;
    }

    protected abstract Task<IHost> CreateHostCoreAsync();

    protected override async Task RunIterationAsync(bool isWarmup, CancellationToken cancellation)
    {
        int depth = isWarmup ? 2 : this.Options.Depth;
        int expected = depth;
        async Task RunOrchestrationAsync(int depth)
        {
            await Task.Yield();
            string id = await this.client.ScheduleNewOrchestrationInstanceAsync(
                nameof(TestOrchestration), depth, cancellation);
            OrchestrationMetadata data = await this.client.WaitForInstanceCompletionAsync(id, true, cancellation);
            if (data.RuntimeStatus != OrchestrationRuntimeStatus.Completed)
            {
                throw new InvalidOperationException($"Unexpected status of {data.RuntimeStatus}.");
            }

            int output = data.ReadOutputAs<int>();
            if (output != expected)
            {
                throw new InvalidOperationException($"Incorrect result: expected {expected}, actual {output}.");
            }
        }

        Task[] tasks = new Task[this.Options.Count];
        for (int i = 0; i < this.Options.Count; i++)
        {
            tasks[i] = RunOrchestrationAsync(depth);
        }

        await Task.WhenAll(tasks).WaitAsync(cancellation);
    }

    protected override Task IterationCleanupAsync(CancellationToken cancellation)
    {
        IEnumerable<OrchestrationRuntimeStatus> statuses = new[]
        {
                OrchestrationRuntimeStatus.Pending,
                OrchestrationRuntimeStatus.Running,
                OrchestrationRuntimeStatus.Terminated,
                OrchestrationRuntimeStatus.Suspended,
                OrchestrationRuntimeStatus.Failed,
                OrchestrationRuntimeStatus.Completed,
        };

        PurgeInstancesFilter filter = new(CreatedFrom: DateTimeOffset.MinValue, Statuses: statuses);
        return this.client.PurgeAllInstancesAsync(filter, cancellation);
    }
}
