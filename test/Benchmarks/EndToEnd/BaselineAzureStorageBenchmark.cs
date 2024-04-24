// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using DurableTask.Core;

namespace Microsoft.DurableTask.Benchmarks.EndToEnd;

[BenchmarkCategory("AzureStorage")]
[MemoryDiagnoser]
[IterationCount(10)]
public class BaselineAzureStorageBenchmark : BaselineBenchmark
{
    protected override IOrchestrationService CreateOrchestrationService()
    {
        return OrchestrationService.Create(new OrchestrationService.Options()
        {
            Name = "benchmarkbaseline",
            Kind = OrchestrationService.Kind.AzureStorage,
            ConnectionString = Environment.GetEnvironmentVariable("AzureStorageConnectionString"),
            UseSessions = false,
        });
    }
}
