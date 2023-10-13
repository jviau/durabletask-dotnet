// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;

namespace Microsoft.DurableTask.Benchmarks.EndToEnd;

[MemoryDiagnoser]
[IterationCount(10)]
public class ChannelShimBenchmark : ChannelBenchmark
{
    protected override void ConfigureClient(IDurableTaskClientBuilder builder)
    {
        builder.Services.AddOrchestrationService(new OrchestrationService.Options()
        {
            Name = "benchmarkBaseline",
            Kind = OrchestrationService.Kind.AzureStorage,
            ConnectionString = Environment.GetEnvironmentVariable("AzureStoreConnectionString"),
            UseSessions = true,
        });

        builder.UseOrchestrationService();
    }

    protected override void ConfigureWorker(IDurableTaskWorkerBuilder builder)
    {
        builder.UseOrchestrationService();
    }
}
