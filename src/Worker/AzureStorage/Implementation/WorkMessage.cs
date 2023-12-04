// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using Azure;
using Azure.Storage.Queues.Models;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// Represents work being dispatched to a new or existing item.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="WorkMessage"/> class.
/// </remarks>
/// <param name="id">The ID of the item this work is dispatched for.</param>
/// <param name="message">The message being dispatched.</param>
class WorkMessage(string id, OrchestrationMessage message)
{
    /// <summary>
    /// Gets the ID of the work this message is for.
    /// </summary>
    public string Id { get; } = Check.NotNullOrEmpty(id);

    /// <summary>
    /// Gets or sets the message to be dispatched.
    /// </summary>
    public OrchestrationMessage Message { get; set; } = Check.NotNull(message);

    /// <summary>
    /// Gets the parent for this work dispatch, if available.
    /// </summary>
    public WorkParent? Parent { get; init; }

    /// <summary>
    /// Gets the message ID.
    /// </summary>
    [JsonIgnore]
    public string MessageId { get; private set; } = null!;

    /// <summary>
    /// Gets the pop receipt.
    /// </summary>
    [JsonIgnore]
    public string PopReceipt { get; private set; } = null!;

    /// <summary>
    /// Gets the dequeue count for this item.
    /// </summary>
    [JsonIgnore]
    public long DequeueCount { get; private set; }

    /// <summary>
    /// Creates a <see cref="WorkMessage"/> from a <see cref="QueueMessage"/>.
    /// </summary>
    /// <param name="message">The message to create from.</param>
    /// <returns>The work dispatch.</returns>
    public static WorkMessage Create(QueueMessage message)
    {
        Check.NotNull(message);
        WorkMessage work = message.Body.ToObject<WorkMessage>(StorageSerializer.Default)!;
        work.Populate(message);
        return work;
    }

    /// <summary>
    /// Populates this instance with properties from the queue message.
    /// </summary>
    /// <param name="message">The queue message.</param>
    public void Populate(QueueMessage message)
    {
        Check.NotNull(message);
        this.MessageId = message.MessageId;
        this.PopReceipt = message.PopReceipt;
        this.DequeueCount = message.DequeueCount;
    }
}

/// <summary>
/// The parent for a <see cref="WorkMessage"/>.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="WorkParent"/> class.
/// </remarks>
/// <param name="Id">The ID of the parent work item.</param>
/// <param name="Name">The name of the parent. May be default.</param>
/// <param name="QueueName">The name of the queue the parent belongs to.</param>
record WorkParent(string Id, TaskName Name, string QueueName);
