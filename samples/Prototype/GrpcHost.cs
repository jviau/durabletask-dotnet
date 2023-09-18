// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Net.Client;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Grpc.Hub;
using Microsoft.DurableTask.Grpc.Hub.Bulk;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Prototype;

public static class GrpcHost
{
    public static IWebHost CreateBulkHubHost(string name, out GrpcChannel channel)
    {
        string address = $"http://localhost:{Random.Shared.Next(30000, 40000)}";
        IWebHost host = WebHost.CreateDefaultBuilder()
            .UseKestrel(options =>
            {
                options.ConfigureEndpointDefaults(listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
            })
            .UseUrls(address)
            .ConfigureServices(services =>
            {
                services.AddOrchestrationService(name);
                services.AddGrpc();
                services.AddSingleton<BulkGrpcTaskHubServer>();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGrpcService<BulkGrpcTaskHubServer>();
                });
            })
            .Build();

        channel = GrpcChannel.ForAddress(address);
        return host;
    }

    public static IWebHost CreateStreamHubHost(out GrpcChannel channel)
    {
        string address = $"http://localhost:{Random.Shared.Next(30000, 40000)}";
        IWebHost host = WebHost.CreateDefaultBuilder()
            .UseKestrel(options =>
            {
                options.ConfigureEndpointDefaults(listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
            })
            .UseUrls(address)
            .ConfigureServices(services =>
            {
                services.AddOrchestrationService("stream");
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

        channel = GrpcChannel.ForAddress(address);
        return host;
    }

    public static IHost CreateWorkerHost(
        Action<IDurableTaskWorkerBuilder> configureWorker, Action<IDurableTaskClientBuilder> configureClient)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(b => b.SetMinimumLevel(LogLevel.Information))
            .ConfigureServices(services =>
            {
                services.AddDurableTaskWorker(b =>
                {
                    b.AddTasks(r =>
                    {
                        r.AddOrchestrator<TestOrchestration>();
                        r.AddActivity<TestActivity>();
                    });
                    configureWorker(b);
                });
                services.AddDurableTaskClient(configureClient);
            })
            .Build();
    }
}
