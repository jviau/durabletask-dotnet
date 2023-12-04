// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using Azure.Data.Tables;
using Azure.Storage.Queues;
using Microsoft.DurableTask.Worker.AzureStorage;
using Microsoft.DurableTask.Worker.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// <see cref="IDurableTaskWorkerBuilder"/> extensions for <see cref="ChannelDurableTaskWorker"/>.
/// </summary>
public static class AzureDurableTaskWorkerBuilderExtensions
{
    /// <summary>
    /// Adds the Azure Storage backend to this builder.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="prefix">The prefix to use.</param>
    /// <param name="storageAccount">The storage URI to use.</param>
    /// <param name="credential">The token credential to use.</param>
    /// <returns>The same builder, for call chaining.</returns>
    public static IDurableTaskWorkerBuilder UseAzureStorage(
        this IDurableTaskWorkerBuilder builder, string prefix, string storageAccount, TokenCredential credential)
    {
        DurableStorageClientOptions options = new()
        {
            HubName = prefix,
            QueueUri = new Uri($"https://{storageAccount}.queue.core.windows.net"),
            TableUri = new Uri($"https://{storageAccount}.table.core.windows.net"),
            Credential = credential,
        };

        builder.Services.AddSingleton(
            sp => CreateActivitySource(options, sp.GetRequiredService<ILoggerFactory>()));
        builder.Services.AddSingleton(
            sp => CreateOrchestrationSource(options, sp.GetRequiredService<ILoggerFactory>()));

        builder.Services.TryAddSingleton(
            sp => new WorkItemChannel(
                sp.GetRequiredService<ActivityWorkItemSource>(), sp.GetRequiredService<OrchestrationWorkItemSource>()));
        builder.Services.AddHostedService(sp => sp.GetRequiredService<WorkItemChannel>());
        builder.Services.AddOptions<ChannelDurableTaskWorkerOptions>(builder.Name)
            .Configure<WorkItemChannel>((o, c) => o.WorkItemReader = c.Reader);
        builder.UseChannels();
        return builder;
    }

    static ActivityWorkItemSource CreateActivitySource(
        DurableStorageClientOptions options, ILoggerFactory loggerFactory)
    {
        ActivityWorkItemFactory factory = new(options, loggerFactory);
        return new ActivityWorkItemSource(factory, options, loggerFactory.CreateLogger<ActivityWorkItemSource>());
    }

    static OrchestrationWorkItemSource CreateOrchestrationSource(
        DurableStorageClientOptions options, ILoggerFactory loggerFactory)
    {
        QueueClient orchestrations = options.GetQueue(options.HubName + "-orchestrations");
        QueueClient activities = options.ActivityQueue;
        TableClient history = options.TableService.GetTableClient(options.HubName + "history");
        TableClient state = options.TableService.GetTableClient(options.HubName + "state");
        return new OrchestrationWorkItemSource(
            orchestrations, activities, history, state, loggerFactory);
    }
}
