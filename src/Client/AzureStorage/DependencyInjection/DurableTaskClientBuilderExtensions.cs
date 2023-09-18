// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Storage.Queues;
using Microsoft.DurableTask.Client.AzureStorage;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Extension methods for adding Durable Task support to .NET hosted services, such as ASP.NET Core hosts.
/// </summary>
public static class DurableTaskClientBuilderExtensions
{
    /// <summary>
    /// Configures the <see cref="IDurableTaskClientBuilder" /> for usage with Azure Storage.
    /// </summary>
    /// <remarks>
    /// This must be called independently of worker registration.
    /// </remarks>
    /// <param name="builder">The builder to configure.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskClientBuilder UseAzureStorage(this IDurableTaskClientBuilder builder)
        => builder.UseAzureStorage(opt => { });

    /// <summary>
    /// Configures the <see cref="IDurableTaskClientBuilder" /> for usage with Azure Storage.
    /// </summary>
    /// <remarks>
    /// This must be called independently of worker registration.
    /// </remarks>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="prefix">The prefix to use.</param>
    /// <param name="storageAccount">The storage URI to use.</param>
    /// <param name="credential">The token credential to use.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskClientBuilder UseAzureStorage(
        this IDurableTaskClientBuilder builder, string prefix, string storageAccount, TokenCredential credential)
    {
        Check.NotNull(builder);
        builder.Services.AddOptions<AzureStorageDurableTaskClientOptions>(builder.Name)
            .PostConfigure(o => AddClients(o, prefix, storageAccount, credential));

        return builder.UseAzureStorage();
    }

    /// <summary>
    /// Configures the <see cref="IDurableTaskClientBuilder" /> for usage with Azure Storage.
    /// </summary>
    /// <remarks>
    /// This must be called independently of worker registration.
    /// </remarks>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="configure">The action to configure the client options.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskClientBuilder UseAzureStorage(
        this IDurableTaskClientBuilder builder, Action<AzureStorageDurableTaskClientOptions> configure)
    {
        Check.NotNull(builder);
        Check.NotNull(configure);
        builder.Services.AddOptions<AzureStorageDurableTaskClientOptions>(builder.Name)
            .Configure(configure)
            .Validate(x => x.InstanceClient is not null, "AzureStorageDurableTaskClientOptions.InstanceClient must not be null.")
            .Validate(x => x.MessageClient is not null, "AzureStorageDurableTaskClientOptions.MessageClient must not be null.")
            .Validate(x => x.HistoryClient is not null, "AzureStorageDurableTaskClientOptions.HistoryClient must not be null.");

        return builder.UseBuildTarget<AzureStorageDurableTaskClient, AzureStorageDurableTaskClientOptions>();
    }

    static void AddClients(
        AzureStorageDurableTaskClientOptions options, string prefix, string storageAccount, TokenCredential credential)
    {
        QueueServiceClient queue = new(new Uri($"https://{storageAccount}.queue.core.windows.net"), credential);
        TableServiceClient table = new(new Uri($"https://{storageAccount}.table.core.windows.net"), credential);
        options.InstanceClient ??= table.GetTableClient(prefix + "state");
        options.HistoryClient ??= table.GetTableClient(prefix + "history");
        options.MessageClient ??= queue.GetQueueClient(prefix + "orchestrations");
    }
}
