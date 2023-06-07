// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using DurableTask.Core;

namespace Microsoft.DurableTask.Benchmarks.EndToEnd;

[MemoryDiagnoser]
[IterationCount(10)]
public class BaselineBenchmark
{
    readonly CancellationTokenSource cts = new();
    IOrchestrationService orchestrationService = null!;
    TaskHubWorker worker = null!;

    protected TaskHubClient Client { get; set; } = null!;

    public IEnumerable<object[]> ScaleValues() => ScaleArguments.Values;

    [GlobalSetup]
    public void Setup()
    {
        async Task Run()
        {
            Console.CancelKeyPress += this.CancelKeyPress;
            this.orchestrationService = OrchestrationService.Create(OrchestrationService.Kind.Default("baseline"));
            await this.orchestrationService.CreateIfNotExistsAsync();
            this.worker = new(this.orchestrationService);
            this.worker.AddTaskOrchestrations(typeof(TestCoreOrchestration));
            this.worker.AddTaskActivities(typeof(TestCoreActivity));

            await this.worker.StartAsync();
            this.Client = new TaskHubClient((IOrchestrationServiceClient)this.orchestrationService);
        }

        Run().GetAwaiter().GetResult();
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
    public void Cleanup()
    {
        async Task Run()
        {
            Console.CancelKeyPress -= this.CancelKeyPress;
            this.cts.Dispose();
            TimeSpan timeout = TimeSpan.FromSeconds(5);
            Task delay = Task.Delay(timeout);
            if (await Task.WhenAny(this.worker.StopAsync(), delay) == delay)
            {
                await this.worker.StopAsync(true);
            }
        }

        Run().GetAwaiter().GetResult();
    }

    [BenchmarkCategory("External", "Local")]
    [Benchmark(Description = "baseline", Baseline = true)]
    [ArgumentsSource(nameof(ScaleValues))]
    public async Task OrchestrationScale(int count, int depth)
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
