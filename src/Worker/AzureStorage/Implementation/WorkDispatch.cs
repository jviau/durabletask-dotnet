// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using Azure.Storage.Queues.Models;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// Represents work being dispatched to a new or existing item.
/// </summary>
class WorkDispatch
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorkDispatch"/> class.
    /// </summary>
    /// <param name="id">The ID of the item this work is dispatched for.</param>
    /// <param name="message">The message being dispatched.</param>
    public WorkDispatch(string id, OrchestrationMessage message)
    {
        this.Id = Check.NotNullOrEmpty(id);
        this.Message = Check.NotNull(message);
    }

    /// <summary>
    /// Gets the ID of the work this message is for.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the message to be dispatched.
    /// </summary>
    public OrchestrationMessage Message { get; }

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
/// The parent for a <see cref="WorkDispatch"/>.
/// </summary>
class WorkParent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorkParent"/> class.
    /// </summary>
    /// <param name="id">The ID of the parent work item.</param>
    /// <param name="name">The name of the parent. May be default.</param>
    public WorkParent(string id, TaskName name)
    {
        this.Id = Check.NotNullOrEmpty(id);
        this.Name = name;
    }

    /// <summary>
    /// Gets the ID of the parent work item.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the task name for this parent.
    /// </summary>
    public TaskName Name { get; }

    /// <summary>
    /// Gets the name of the queue the parent is from.
    /// </summary>
    public string? QueueName { get; init; }
}
