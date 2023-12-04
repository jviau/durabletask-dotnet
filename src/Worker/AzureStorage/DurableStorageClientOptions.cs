// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Storage.Queues;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// Client options for this durable client.
/// </summary>
public class DurableStorageClientOptions : ClientOptions
{
    readonly ConcurrentDictionary<string, QueueClient> queues = new();
    QueueClient? activityQueue;
    QueueServiceClient? queueClient;
    TableServiceClient? tableClient;

    /// <summary>
    /// Gets or sets the name of the hub.
    /// </summary>
    public string HubName { get; set; } = "durable";

    /// <summary>
    /// Gets or sets the token credentials to use.
    /// </summary>
    public TokenCredential? Credential { get; set; }

    /// <summary>
    /// Gets or sets the queue uri.
    /// </summary>
    public Uri? QueueUri { get; set; }

    /// <summary>
    /// Gets or sets the table uri.
    /// </summary>
    public Uri? TableUri { get; set; }

    /// <summary>
    /// Gets the queue service client.
    /// </summary>
    internal QueueServiceClient QueueService
        => this.queueClient ??= new QueueServiceClient(
            this.QueueUri!, this.Credential!, this.GetOptions<QueueClientOptions>());

    /// <summary>
    /// Gets the table service client.
    /// </summary>
    internal TableServiceClient TableService
        => this.tableClient ??= new TableServiceClient(
            this.TableUri!, this.Credential!, this.GetOptions<TableClientOptions>());

    /// <summary>
    /// Gets the activity queue.
    /// </summary>
    internal QueueClient ActivityQueue
        => this.activityQueue ??= this.QueueService.GetQueueClient($"{this.HubName}-activities");

    /// <summary>
    /// Gets a <see cref="QueueClient"/>.
    /// </summary>
    /// <param name="name">The name of the queue client to get.</param>
    /// <returns>The queue client.</returns>
    public QueueClient GetQueue(string name)
    {
        // TODO: cache queues
        return this.queues.GetOrAdd(name, this.QueueService.GetQueueClient(name));
    }

    TOptions GetOptions<TOptions>()
    {
        return (TOptions)Activator.CreateInstance(typeof(TOptions), (ClientOptions)this, this.Diagnostics);
    }

    ValidateOptionsResult Validate()
    {
        IEnumerable<string>? errors = null;
        if (this.Credential is null)
        {
            errors ??= Enumerable.Empty<string>();
            errors = errors.Append("A credential must be supplied.");
        }

        if (this.QueueUri is null)
        {
            errors ??= Enumerable.Empty<string>();
            errors = errors.Append("A queue service uri must be supplied.");
        }

        if (this.TableUri is null)
        {
            errors ??= Enumerable.Empty<string>();
            errors = errors.Append("A queue service uri must be supplied.");
        }

        return errors is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
