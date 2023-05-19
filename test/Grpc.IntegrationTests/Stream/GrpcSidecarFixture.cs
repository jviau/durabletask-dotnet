// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.DurableTask.Client.Grpc;
using Microsoft.DurableTask.Grpc.Hub;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Grpc.Stream.Tests;

public sealed class GrpcSidecarFixture : IDisposable
{
    const string ListenHost = "localhost";

    readonly IWebHost host;

    public GrpcSidecarFixture()
    {
        // Use a random port number to allow multiple instances to run in parallel
        string address = $"http://{ListenHost}:{Random.Shared.Next(30000, 40000)}";
        this.host = new WebHostBuilder()
            .UseKestrel(options =>
            {
                // Need to force Http2 in Kestrel in unencrypted scenarios
                // https://docs.microsoft.com/en-us/aspnet/core/grpc/troubleshoot?view=aspnetcore-3.0
                options.ConfigureEndpointDefaults(listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
            })
            .UseUrls(address)
            .ConfigureServices(services =>
            {
                services.AddGrpc();
                services.AddTaskHubGrpc();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGrpcService<GrpcTaskClientServer>();
                    endpoints.MapGrpcService<GrpcTaskHubServer>();
                });
            })
            .Build();

        this.host.Start();

        this.Channel = GrpcChannel.ForAddress(address);
    }

    public GrpcChannel Channel { get; }

    public void Dispose()
    {
        this.Channel.ShutdownAsync().GetAwaiter().GetResult();
        this.host.StopAsync().GetAwaiter().GetResult();
        this.host.Dispose();
    }
}
