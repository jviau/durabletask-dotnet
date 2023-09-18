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
        QueueServiceClient queue = new(new Uri($"https://{storageAccount}.queue.core.windows.net"), credential);
        TableServiceClient table = new(new Uri($"https://{storageAccount}.table.core.windows.net"), credential);

        builder.Services.AddSingleton(
            sp => CreateActivitySource(prefix, queue, sp.GetRequiredService<ILoggerFactory>()));
        builder.Services.AddSingleton(
            sp => CreateOrchestrationSource(prefix, queue, table, sp.GetRequiredService<ILoggerFactory>()));

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
        string prefix, QueueServiceClient client, ILoggerFactory loggerFactory)
    {
        return new ActivityWorkItemSource(prefix, client, loggerFactory.CreateLogger<ActivityWorkItemSource>());
    }

    static OrchestrationWorkItemSource CreateOrchestrationSource(
        string prefix, QueueServiceClient queue, TableServiceClient table, ILoggerFactory loggerFactory)
    {
        QueueClient orchestrations = queue.GetQueueClient(prefix + "orchestrations");
        QueueClient activities = queue.GetQueueClient(prefix + "activities");
        TableClient history = table.GetTableClient(prefix + "history");
        TableClient state = table.GetTableClient(prefix + "state");
        return new OrchestrationWorkItemSource(
            orchestrations, activities, history, state, loggerFactory);
    }
}
