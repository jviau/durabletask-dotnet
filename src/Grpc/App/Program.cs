// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using CommandLine;
using DurableTask.AzureStorage;
using DurableTask.Core;
using Microsoft.DurableTask.Grpc.App;
using Microsoft.DurableTask.Grpc.Hub;
using Microsoft.DurableTask.Sidecar.Grpc;

StartupOptions options = Parser.Default.ParseArguments<StartupOptions>(args).Value;
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

if (options.Port is int port)
{
    builder.WebHost.UseUrls($"http://localhost:{port}");
}

// Add services to the container.
AddAzureStorageOrchestrationService(builder.Services, options.Name);
builder.Services.AddGrpc();
builder.Services.AddTaskHubGrpc();

builder.Services.Configure<TaskHubGrpcServerOptions>(o => o.Mode = TaskHubGrpcServerMode.ApiServerAndDispatcher);
builder.Services.AddSingleton<TaskHubGrpcServer>();

WebApplication app = builder.Build();

IOrchestrationService orchestrationService = app.Services.GetRequiredService<IOrchestrationService>();
await orchestrationService.CreateIfNotExistsAsync();

// Configure the HTTP request pipeline.
app.MapGrpcService<GrpcTaskHubServer>();
app.MapGrpcService<GrpcTaskClientServer>();
app.MapGrpcService<TaskHubGrpcServer>();
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();

static void AddAzureStorageOrchestrationService(IServiceCollection services, string name)
{
    services.AddSingleton(sp =>
    {
        ILoggerFactory lf = sp.GetRequiredService<ILoggerFactory>();

        AzureStorageOrchestrationServiceSettings settings = new()
        {
            PartitionCount = 1,
            StorageConnectionString = "UseDevelopmentStorage=true",
            LoggerFactory = lf,
            MaxQueuePollingInterval = TimeSpan.FromSeconds(5),
            TaskHubName = "prototype" + name,
        };

        IOrchestrationService s = new AzureStorageOrchestrationService(settings);
        return s;
    });

    services.AddSingleton(sp => (IOrchestrationServiceClient)sp.GetRequiredService<IOrchestrationService>());
}
