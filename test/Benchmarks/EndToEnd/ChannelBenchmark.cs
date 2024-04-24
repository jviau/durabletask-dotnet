// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Benchmarks.EndToEnd;

public abstract class ChannelBenchmark
{
    int active;

    protected IHost Host { get; private set; } = null!;

    protected DurableTaskClient Client { get; private set; } = null!;

    protected CancellationToken ShutdownToken { get; private set; }

    public IEnumerable<object[]> ScaleValues() => ScaleArguments.Values;

    [GlobalSetup]
    public void Setup()
    {
        async Task Run()
        {
            this.Host = HostHelpers.CreateWorkerHost(this.ConfigureWorker, this.ConfigureClient);
            this.Client = this.Host.Services.GetRequiredService<DurableTaskClient>();
            this.ShutdownToken = this.Host.Services.GetRequiredService<IHostApplicationLifetime>()
                .ApplicationStopping;

            await this.OnGlobalSetupAsync();
            await this.IterationCleanupAsync();
            await this.Host.StartAsync();
        }

        Run().GetAwaiter().GetResult();
    }

    [IterationCleanup]
    public void IterationCleanup() => this.IterationCleanupAsync().GetAwaiter().GetResult();

    [GlobalCleanup]
    public void Cleanup()
    {
        async Task Run()
        {
            await this.Host.StopAsync();
            this.Host.Dispose();
        }

        Run().GetAwaiter().GetResult();
    }

    [Benchmark(Description = "channel-shim")]
    [ArgumentsSource(nameof(ScaleValues))]
    public async Task OrchestrationScale(int count, int depth)
    {
        ILogger logger = this.Host.Services.GetRequiredService<ILogger<ChannelBenchmark>>();
        //using Timer t = new(
        //    _ =>
        //    {
        //        Console.WriteLine($"Pending orchestrations (Outer): {this.active}");
        //    },
        //    null,
        //    0,
        //    1000);

        async Task RunOrchestrationAsync()
        {
            await Task.Yield();
            string id = await this.Client.ScheduleNewOrchestrationInstanceAsync(nameof(TestOrchestration), depth);

            Interlocked.Increment(ref this.active);

            try
            {
                OrchestrationMetadata data = await this.Client.WaitForInstanceCompletionAsync(
                    id, getInputsAndOutputs: true, this.ShutdownToken);
                if (data.RuntimeStatus != OrchestrationRuntimeStatus.Completed)
                {
                    throw new InvalidOperationException($"Unexpected status of {data.RuntimeStatus}.");
                }

                int result = data.ReadOutputAs<int>();
                if (result != depth)
                {
                    throw new InvalidOperationException($"Unexpected result of {result}. Expected {depth}.");
                }
            }
            finally
            {
                Interlocked.Decrement(ref this.active);
            }
        }

        Task[] tasks = new Task[count];
        for (int i = 0; i < count; i++)
        {
            tasks[i] = RunOrchestrationAsync();
        }

        await Task.WhenAll(tasks).WaitAsync(this.ShutdownToken);
    }

    protected Task IterationCleanupAsync()
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
        return this.Client.PurgeAllInstancesAsync(filter, this.ShutdownToken);
    }

    protected virtual Task OnGlobalSetupAsync() => Task.CompletedTask;

    protected abstract void ConfigureWorker(IDurableTaskWorkerBuilder builder);

    protected abstract void ConfigureClient(IDurableTaskClientBuilder builder);
}
