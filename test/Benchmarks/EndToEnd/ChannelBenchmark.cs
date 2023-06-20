// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DurableTask.Benchmarks.EndToEnd;

[MemoryDiagnoser]
[IterationCount(10)]
public class ChannelBenchmark
{
    protected IHost WorkerHost { get; private set; } = null!;

    protected DurableTaskClient Client { get; private set; } = null!;

    protected CancellationToken ShutdownToken { get; private set; }

    public IEnumerable<object[]> ScaleValues() => ScaleArguments.Values;

    [GlobalSetup]
    public void Setup()
    {
        Task Run()
        {
            this.WorkerHost = GrpcHost.CreateWorkerHost(
                worker =>
                {
                    worker.Services.AddOrchestrationService(OrchestrationService.Kind.Default("baseline"));
                    worker.UseOrchestrationService();
                },
                client =>
                {
                    client.UseOrchestrationService();
                });

            this.Client = this.WorkerHost.Services.GetRequiredService<DurableTaskClient>();
            this.ShutdownToken = this.WorkerHost.Services.GetRequiredService<IHostApplicationLifetime>()
                .ApplicationStopping;
            return this.WorkerHost.StartAsync();
        }

        Run().GetAwaiter().GetResult();
    }

    [IterationCleanup]
    public void IterationCleanup()
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
        this.Client.PurgeAllInstancesAsync(filter, this.ShutdownToken).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        async Task Run()
        {
            await this.WorkerHost.StopAsync();
            this.WorkerHost.Dispose();
        }

        Run().GetAwaiter().GetResult();
    }

    [BenchmarkCategory("Local")]
    [Benchmark(Description = "channel")]
    [ArgumentsSource(nameof(ScaleValues))]
    public async Task OrchestrationScale(int count, int depth)
    {
        async Task RunOrchestrationAsync()
        {
            await Task.Yield();
            string id = await this.Client.ScheduleNewOrchestrationInstanceAsync(
                nameof(TestOrchestration), new TestInput(depth, "test-value"));
            OrchestrationMetadata data = await this.Client.WaitForInstanceCompletionAsync(id, this.ShutdownToken);
            if (data.RuntimeStatus != OrchestrationRuntimeStatus.Completed)
            {
                throw new InvalidOperationException($"Unexpected status of {data.RuntimeStatus}.");
            }
        }

        Task[] tasks = new Task[count];
        for (int i = 0; i < count; i++)
        {
            tasks[i] = RunOrchestrationAsync();
        }

        await Task.WhenAll(tasks).WaitAsync(this.ShutdownToken);
    }
}
