// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using Grpc.Net.Client;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DurableTask.Benchmarks.EndToEnd;

[MemoryDiagnoser]
public class GrpcBulkExternalHosted : GrpcExternalHosted
{
    protected override string Name => "bulk";

    [Benchmark(Description = "Scale: channel runner + bulk gRPC")]
    [ArgumentsSource(nameof(ScaleValues))]
    public Task OrchestrationScale(int count, int depth) => this.RunScaleAsync(count, depth);

    protected override IHost CreateWorkerHost(GrpcChannel channel)
    {
        return  GrpcHost.CreateWorkerHost(
            b => b.UseBulkGrpcChannel(channel), b => b.UseGrpc(channel).RegisterDirectly());
    }
}
