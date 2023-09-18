// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure;
using Azure.Data.Tables;

namespace Microsoft.DurableTask.Client.AzureStorage.Implementation;

/// <summary>
/// The table entity for an orchestration instance.
/// </summary>
class OrchestrationInstanceEntity : ITableEntity
{
    /// <inheritdoc/>
    public string? PartitionKey { get; set; }

    /// <inheritdoc/>
    public string? RowKey { get; set; }

    /// <inheritdoc/>
    public DateTimeOffset? Timestamp { get; set; }

    /// <inheritdoc/>
    public ETag ETag { get; set; }

    /// <summary>
    /// Gets or sets when this instance was created at.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the name of the orchestration.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the orchestration runtime status.
    /// </summary>
    public OrchestrationRuntimeStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the orchestration sub-status.
    /// </summary>
    public string? SubStatus { get; set; }

    /// <summary>
    /// Gets or sets the orchestration input.
    /// </summary>
    public string? Input { get; set; }

    /// <summary>
    /// Gets or sets the orchestration result.
    /// </summary>
    public string? Result { get; set; }

    /// <summary>
    /// Gets or sets the orchestration failure.
    /// </summary>
    public TaskFailureDetails? Failure { get; set; }
}
