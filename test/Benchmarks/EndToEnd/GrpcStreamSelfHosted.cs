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
public class GrpcStreamSelfHosted : GrpcSelfHosted
{
    [Benchmark(Description = "Scale: channel runner + stream gRPC")]
    [ArgumentsSource(nameof(ScaleValues))]
    public Task OrchestrationScale(int count, int depth) => this.RunScaleAsync(count, depth);

    protected override IWebHost CreateHubHost(out GrpcChannel channel)
    {
        return GrpcHost.CreateStreamHubHost("stream", out channel);
    }

    protected override IHost CreateWorkerHost(GrpcChannel channel)
    {
        return  GrpcHost.CreateWorkerHost(
            b => b.UseStreamGrpcChannel(channel), b => b.UseStreamGrpc(channel).RegisterDirectly());
    }
}
