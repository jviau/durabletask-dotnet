// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BenchmarkDotNet.Attributes;
using DurableTask.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.Benchmarks.EndToEnd;

public abstract class GrpcSelfHosted : GrpcBenchmark
{
    protected IWebHost HubHost { get; private set; } = null!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        this.HubHost = this.CreateHubHost(out GrpcChannel channel);
        IOrchestrationService os = this.HubHost.Services.GetRequiredService<IOrchestrationService>();

        await this.HubHost.StartAsync();
        await os.CreateIfNotExistsAsync();
        await this.SetupCoreAsync(channel);
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await this.CleanupCoreAsync();
        this.WorkerHost.Dispose();
        this.HubHost.Dispose();
    }

    protected abstract IWebHost CreateHubHost(out GrpcChannel channel);
}
