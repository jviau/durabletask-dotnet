// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DurableTask.Prototype;

interface IRunner : IAsyncDisposable
{
    Task RunAsync();
}

abstract class Runner<TOptions> : IRunner
    where TOptions : StartupOptions
{
    IHost? worker;
    DurableTaskClient client = null!;

    public Runner(TOptions options)
    {
        this.Options = options;
    }

    protected TOptions Options { get; }

    public async Task RunAsync()
    {
        this.worker = await this.InitializeAsync();
        await this.worker.StartAsync();

        this.client = this.worker.Services.GetRequiredService<DurableTaskClient>();
        IHostApplicationLifetime lifetime = this.worker.Services.GetRequiredService<IHostApplicationLifetime>();
        CancellationToken stoppingToken = lifetime.ApplicationStopping;

        async Task RunOrchestrationAsync(int depth)
        {
            await Task.Yield();
            string id = await this.client.ScheduleNewOrchestrationInstanceAsync(
                nameof(TestOrchestration), new TestInput(depth, "test-value"));
            OrchestrationMetadata data = await this.client.WaitForInstanceCompletionAsync(id, stoppingToken);
            if (data.RuntimeStatus != OrchestrationRuntimeStatus.Completed)
            {
                throw new InvalidOperationException($"Unexpected status of {data.RuntimeStatus}.");
            }
        }

        async Task<TimeSpan> RunIterationAsync(int count, int depth)
        {
            Stopwatch sw = Stopwatch.StartNew();
            Task[] tasks = new Task[count];
            for (int i = 0; i < count; i++)
            {
                tasks[i] = RunOrchestrationAsync(depth);
            }

            await Task.WhenAll(tasks).WaitAsync(stoppingToken);
            sw.Stop();
            return sw.Elapsed;
        }

        // warmup
        await this.CleanupAsync(stoppingToken);
        for (int i = 0; i < 5; i++)
        {
            await RunIterationAsync(this.Options.Count, 1);
            Console.WriteLine($"Warming up {i + 1}/5");
            await this.CleanupAsync(stoppingToken);
        }

        TimeSpan[] times = new TimeSpan[10];
        for (int i = 0; i < 10; i++)
        {
            times[i] = await RunIterationAsync(this.Options.Count, this.Options.Depth);
            Console.WriteLine($"Itreration {i + 1}/10 -- {times[i].TotalMilliseconds}");
            await this.CleanupAsync(stoppingToken);
        }

        var ms = times.Select(t => t.TotalMilliseconds).ToList();
        double avg = ms.Average();
        double stdDev = Math.Sqrt(ms.Average(v => Math.Pow(v - avg, 2)));
        Console.WriteLine($"{this.Options.Description} -- Average: {avg}. StdDev: {stdDev}");
    }

    public async ValueTask DisposeAsync()
    {
        IHost? worker = Interlocked.Exchange(ref this.worker, null);
        if (worker is not null)
        {
            await worker.StopAsync();
            worker.Dispose();
        }

        await this.DisposeCoreAsync();
    }

    protected virtual Task CleanupAsync(CancellationToken cancellation)
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

    protected abstract ValueTask DisposeCoreAsync();

    protected abstract Task<IHost> InitializeAsync();
}
