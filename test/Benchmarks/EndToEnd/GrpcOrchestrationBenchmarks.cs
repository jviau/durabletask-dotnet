// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DurableTask.Benchmarks.EndToEnd;

[MemoryDiagnoser]
public class GrpcOrchestrationBenchmarks : GrpcBenchmark
{
    [GlobalSetup(Target = nameof(ScaleStream))]
    public Task SetupScaleStreamAsync()
    {
        IWebHost hub = GrpcHost.CreateStreamHubHost("stream", out GrpcChannel channel);
        IHost worker = GrpcHost.CreateWorkerHost(
            b => b.UseStreamGrpcChannel(channel), b => b.UseStreamGrpc(channel).RegisterDirectly());
        return this.SetupAsync(hub, worker);
    }

    [GlobalSetup(Target = nameof(ScaleBulk))]
    public Task SetupScaleBulkAsync()
    {
        IWebHost hub = GrpcHost.CreateBulkHubHost("bulk", out GrpcChannel channel);
        IHost worker = GrpcHost.CreateWorkerHost(
            b => b.UseBulkGrpcChannel(channel), b => b.UseGrpc(channel).RegisterDirectly());
        return this.SetupAsync(hub, worker);
    }

    [GlobalSetup(Target = nameof(ScaleCore))]
    public Task SetupScaleCoreAsync()
    {
        IWebHost hub = GrpcHost.CreateBulkHubHost("core", out GrpcChannel channel);
        IHost worker = GrpcHost.CreateWorkerHost(
            b => b.UseGrpc(channel), b => b.UseGrpc(channel).RegisterDirectly());
        return this.SetupAsync(hub, worker);
    }

    [Benchmark(Description = "Scale: Channel runner + Stream gRPC")]
    [Arguments(10, 1)]
    [Arguments(10, 5)]
    [Arguments(100, 1)]
    [Arguments(100, 5)]
    public Task ScaleStream(int count, int depth) => this.RunScaleAsync(count, depth);

    [Benchmark(Description = "Scale: Channel runner + Bulk gRPC")]
    [Arguments(10, 1)]
    [Arguments(10, 5)]
    [Arguments(100, 1)]
    [Arguments(100, 5)]
    public Task ScaleBulk(int count, int depth) => this.RunScaleAsync(count, depth);

    [Benchmark(Description = "Scale: Core runner + Bulk gRPC")]
    [Arguments(10, 1)]
    [Arguments(10, 5)]
    [Arguments(100, 1)]
    [Arguments(100, 5)]
    public Task ScaleCore(int count, int depth) => this.RunScaleAsync(count, depth);

    async Task RunScaleAsync(int count, int depth)
    {
        async Task RunOrchestrationAsync(int i)
        {
            await Task.Yield();
            string id = await this.Client.ScheduleNewOrchestrationInstanceAsync(
                nameof(TestOrchestration), new TestInput(depth, "test-value"));
            await this.ServiceClient.WaitForOrchestrationAsync(
                id, null, TimeSpan.MaxValue, this.ShutdownToken);
        }

        Task[] tasks = new Task[count];
        for (int i = 0; i < count; i++)
        {
            tasks[i] = RunOrchestrationAsync(i);
        }

        await Task.WhenAll(tasks).WaitAsync(this.ShutdownToken);
    }
}
