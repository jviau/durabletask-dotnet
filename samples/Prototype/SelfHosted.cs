// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Sidecar;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DurableTask.Prototype;

sealed class SelfHosted : Runner<SelfHostedOptions>
{
    IWebHost? hub;

    public SelfHosted(SelfHostedOptions options)
        : base(options)
    {
    }

    protected override async Task<IHost> InitializeAsync()
    {
        Console.WriteLine("Running locally hosted benchmark");
        IHost worker = CreateHosts(this.Options.Mode, out this.hub);
        await this.hub.StartAsync();
        return worker;
    }

    protected override async ValueTask DisposeCoreAsync()
    {
        IWebHost? hub = Interlocked.Exchange(ref this.hub, null);
        if (hub is not null)
        {
            await hub.StopAsync();
            hub.Dispose();
        }
    }

    protected override Task CleanupAsync(CancellationToken cancellation)
    {
        if (this.hub!.Services.GetService<IOrchestrationService>() is InMemoryOrchestrationService service)
        {
            return service.CreateAsync(true);
        }

        return base.CleanupAsync(cancellation);
    }

    static IHost CreateHosts(int mode, out IWebHost hub)
    {
        if (mode == 0)
        {
            hub = GrpcHost.CreateBulkHubHost("core", out GrpcChannel channel);
            return GrpcHost.CreateWorkerHost(
                        b => b.UseGrpc(channel),
                        b => b.UseGrpc(channel).RegisterDirectly());
        }
        else if (mode == 1)
        {
            hub = GrpcHost.CreateBulkHubHost("bulk", out GrpcChannel channel);
            return GrpcHost.CreateWorkerHost(
                        b => b.UseBulkGrpcChannel(channel),
                        b => b.UseGrpc(channel).RegisterDirectly());
        }
        else if (mode == 2)
        {
            hub = GrpcHost.CreateStreamHubHost(out GrpcChannel channel);
            return GrpcHost.CreateWorkerHost(
                        b => b.UseStreamGrpcChannel(channel),
                        b => b.UseStreamGrpc(channel).RegisterDirectly());
        }
        else
        {
            throw new ArgumentException();
        }
    }
}
