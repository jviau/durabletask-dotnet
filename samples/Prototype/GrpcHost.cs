// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.AzureStorage;
using DurableTask.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Grpc.Hub;
using Microsoft.DurableTask.Sidecar;
using Microsoft.DurableTask.Sidecar.Grpc;
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
                //services.AddInMemoryOrchestrationService();
                services.AddAzureStorageOrchestrationService(name);
                services.AddGrpc();
                services.AddSingleton<TaskHubGrpcServer>();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGrpcService<TaskHubGrpcServer>();
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
                //services.AddInMemoryOrchestrationService();
                services.AddAzureStorageOrchestrationService("stream");
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
            .ConfigureLogging(b => b.ClearProviders())
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

    static void AddAzureStorageOrchestrationService(this IServiceCollection services, string name)
    {
        services.AddSingleton(sp =>
        {
            ILoggerFactory lf = sp.GetRequiredService<ILoggerFactory>();

            AzureStorageOrchestrationServiceSettings settings = new()
            {
                PartitionCount = 1,
                StorageConnectionString = "UseDevelopmentStorage=true",
                LoggerFactory = lf,
                TaskHubName = "prototype" + name,
            };

            IOrchestrationService s = new AzureStorageOrchestrationService(settings);
            return s;
        });
        
        services.AddSingleton(sp => (IOrchestrationServiceClient)sp.GetRequiredService<IOrchestrationService>());
    }

    static void AddInMemoryOrchestrationService(this IServiceCollection services)
    {
        services.AddSingleton<IOrchestrationService, InMemoryOrchestrationService>();
        services.AddSingleton(sp => (IOrchestrationServiceClient)sp.GetRequiredService<IOrchestrationService>());
    }
}
