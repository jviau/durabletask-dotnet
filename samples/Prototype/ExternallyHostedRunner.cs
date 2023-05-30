// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Net.Client;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DurableTask.Prototype;

class ExternallyHostedRunner : PrototypeRunner<ExternalHostedOptions>
{
    public ExternallyHostedRunner(ExternalHostedOptions options)
        : base(options)
    {
    }

    protected override Task<IHost> CreateHostCoreAsync()
    {
        Console.WriteLine($"Running externally hosted benchmark, port {this.Options.Port}.");
        return Task.FromResult(CreateHost(this.Options.Mode, this.Options.Port));
    }

    protected override ValueTask DisposeCoreAsync() => default;

    static IHost CreateHost(int mode, int port)
    {
        GrpcChannel channel = GrpcChannel.ForAddress($"http://localhost:{port}");
        if (mode == 0)
        {
            return GrpcHost.CreateWorkerHost(
                        b => b.UseGrpc(channel),
                        b => b.UseGrpc(channel).RegisterDirectly());
        }
        else if (mode == 1)
        {
            return GrpcHost.CreateWorkerHost(
                        b => b.UseBulkGrpcChannel(channel),
                        b => b.UseGrpc(channel).RegisterDirectly());
        }
        else if (mode == 2)
        {
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
