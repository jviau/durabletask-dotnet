// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using DurableTask.Core;
using Grpc.Net.Client;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DurableTask.Benchmarks.EndToEnd;

[MaxIterationCount(30)]
public abstract class GrpcBenchmark
{
    protected IHost WorkerHost { get; private set; } = null!;

    protected DurableTaskClient Client { get; private set; } = null!;

    protected CancellationToken ShutdownToken { get; private set; }

    public IEnumerable<object[]> ScaleValues()
    {
        //yield return new object[] { 10, 5 };
        //yield return new object[] { 100, 1 };
        yield return new object[] { 100, 5 };
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

    protected async Task SetupCoreAsync(GrpcChannel channel)
    {
        this.WorkerHost = this.CreateWorkerHost(channel);
        this.Client = this.WorkerHost.Services.GetRequiredService<DurableTaskClient>();
        this.ShutdownToken = this.WorkerHost.Services.GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStopping;

        await this.WorkerHost.StartAsync();
    }

    protected async Task CleanupCoreAsync()
    {
        await this.WorkerHost.StopAsync();
        this.WorkerHost.Dispose();
    }

    protected async Task RunScaleAsync(int count, int depth)
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

    protected abstract IHost CreateWorkerHost(GrpcChannel channel);
}
