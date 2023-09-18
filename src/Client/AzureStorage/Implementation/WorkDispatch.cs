// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Client.AzureStorage.Implementation;

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
}
