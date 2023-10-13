// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using DurableTask.Core;

namespace Microsoft.DurableTask.Benchmarks.EndToEnd;

[MemoryDiagnoser]
[IterationCount(10)]
public class BaselineMemoryBenchmark : BaselineBenchmark
{
    protected override IOrchestrationService CreateOrchestrationService()
    {
        return OrchestrationService.Create(new()
        {
            Name = "benchmarkBaseline",
            Kind = OrchestrationService.Kind.InMemory,
        });
    }
}
