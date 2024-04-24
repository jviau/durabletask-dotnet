// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using DurableTask.Core;
using DurableTask.Core.Serializing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DurableTask.Benchmarks.EndToEnd;

public abstract class BaselineBenchmark
{
    readonly CancellationTokenSource cts = new();
    IOrchestrationService orchestrationService = null!;
    TaskHubWorker worker = null!;

    protected TaskHubClient Client { get; set; } = null!;

    ILogger logger = NullLogger.Instance;
    int active;

    public IEnumerable<object[]> ScaleValues() => ScaleArguments.Values;

    [GlobalSetup]
    public void Setup()
    {
        async Task Run()
        {
            ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().AddFilter(l => l > LogLevel.Warning));
            this.logger = loggerFactory.CreateLogger<ChannelBenchmark>();
            Console.CancelKeyPress += this.CancelKeyPress;
            this.orchestrationService = this.CreateOrchestrationService();
            Console.WriteLine("Initializing orchestration service.");
            await this.orchestrationService.CreateIfNotExistsAsync();
            Console.WriteLine("Initialized orchestration service.");
            this.worker = new(this.orchestrationService, loggerFactory);
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

    [Benchmark(Description = "baseline", Baseline = true)]
    [ArgumentsSource(nameof(ScaleValues))]
    public async Task OrchestrationScale(int count, int depth)
    {
        using Timer t = new(
            _ =>
            {
                this.logger.LogInformation("Pending orchestrations (Outer): {Active}", this.active);
            },
            null,
            0,
            1000);

        async Task RunOrchestrationAsync()
        {
            await Task.Yield();
            OrchestrationInstance instance = await this.Client.CreateOrchestrationInstanceAsync(
                typeof(TestCoreOrchestration), depth);


            Interlocked.Increment(ref this.active);
            try
            {
                OrchestrationState state = await this.Client.WaitForOrchestrationAsync(
                    instance, TimeSpan.MaxValue, this.cts.Token);
                if (state.OrchestrationStatus != OrchestrationStatus.Completed)
                {
                    throw new InvalidOperationException($"Unexpected status of {state.OrchestrationStatus}.");
                }

                int result = JsonDataConverter.Default.Deserialize<int>(state.Output);
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

        await Task.WhenAll(tasks).WaitAsync(this.cts.Token);
    }

    protected abstract IOrchestrationService CreateOrchestrationService();

    void CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        if (!this.cts.IsCancellationRequested)
        {
            this.cts.Cancel();
        }
    }
}
