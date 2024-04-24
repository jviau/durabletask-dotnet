// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Identity;
using BenchmarkDotNet.Attributes;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureStorage;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Benchmarks.EndToEnd;

[BenchmarkCategory("AzureStorage")]
[MemoryDiagnoser]
[IterationCount(10)]
public class ChannelAzureStorageBenchmark : ChannelBenchmark
{
    protected override void ConfigureClient(IDurableTaskClientBuilder builder)
    {
        builder.UseAzureStorage("benchmarkchannels", "storagejavia", new AzureCliCredential());
    }

    protected override void ConfigureWorker(IDurableTaskWorkerBuilder builder)
    {
        builder.UseAzureStorage("benchmarkchannels", "storagejavia", new AzureCliCredential());
    }

    protected override async Task OnGlobalSetupAsync()
    {
        AzureStorageDurableTaskClientOptions options = this.Host.Services
            .GetRequiredService<IOptions<AzureStorageDurableTaskClientOptions>>().Value;

        await options.CreateIfNotExistsAsync();
    }
}
