// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;

namespace Microsoft.DurableTask.Benchmarks.EndToEnd;

[BenchmarkCategory("InMemory")]
[MemoryDiagnoser]
[IterationCount(10)]
public class ChannelShimInMemoryBenchmark : ChannelBenchmark
{
    protected override void ConfigureClient(IDurableTaskClientBuilder builder)
    {
        builder.Services.AddOrchestrationService(new OrchestrationService.Options()
        {
            Name = "inmemory",
            Kind = OrchestrationService.Kind.InMemory,
            UseSessions = true,
        });

        builder.UseOrchestrationService();
    }

    protected override void ConfigureWorker(IDurableTaskWorkerBuilder builder)
    {
        builder.UseOrchestrationService();
    }
}
