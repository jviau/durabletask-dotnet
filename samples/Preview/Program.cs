// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Preview;
using Preview.MediatorPattern;
using Preview.MediatorPattern.ExistingTypes;

HostApplicationBuilder builder = Host.CreateApplicationBuilder();

builder.Services.AddDurableTaskClient(b =>
{
    b.UseGrpc();
    b.RegisterDirectly();
});

builder.Services.AddDurableTaskWorker(b =>
{
    b.AddTasks(tasks =>
    {
        Mediator1Command.Register(tasks);
        Mediator2Command.Register(tasks);
    });

    b.UseGrpc();
});

// Sets up CosmosDB services using IAzureClientFactory.
builder.Services.AddAzureClients(b =>
{
    b.AddClient<CosmosClient, CosmosClientOptions>((options, credential, _) =>
    {
        return new CosmosClient(builder.Configuration["CosmosDb:AccountEndpoint"], credential, options);
    })
    .ConfigureOptions(builder.Configuration.GetSection("CosmosDb"));

    b.AddClient<Container, CosmosClientOptions>(
        (_, _, provider) =>
        {
            CosmosClient client = provider.GetRequiredService<CosmosClient>();
            Database db = client.GetDatabase(CosmosConstants.Database);
            return db.GetContainer(CosmosConstants.Container);
        })
        .WithName(CosmosConstants.Container);

    b.UseCredential(new DefaultAzureCredential());
});

new HostBuilderShim(builder).UseCommandLineApplication<Sample>(args);

IHost host = builder.Build();
await host.RunCommandLineApplicationAsync();
