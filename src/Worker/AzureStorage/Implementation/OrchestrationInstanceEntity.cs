// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure;
using Azure.Data.Tables;

namespace Microsoft.DurableTask.Worker.AzureStorage;

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
    public RuntimeStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the orchestration input.
    /// </summary>
    public string? Input { get; set; }

    /// <summary>
    /// Gets or sets the parent ID for this orchestration.
    /// </summary>
    public string? ParentId { get; set; }

    /// <summary>
    /// Gets or sets the parent name.
    /// </summary>
    public string? ParentName { get; set; }

    /// <summary>
    /// Gets or sets the scheduled ID for a sub-orchestration.
    /// </summary>
    public int? ScheduledId { get; set; }
}
