// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;

namespace Microsoft.DurableTask.AspNetCore.Http;

/// <summary>
/// Interface for results that fetch and return the status of an orchestration.
/// </summary>
interface IOrchestrationStatusResult
{
    /// <summary>
    /// Gets the instance ID of the orchestration.
    /// </summary>
    public string InstanceId { get; }

    /// <summary>
    /// Gets or sets a value indicating whether to retrieve all available data for the orchestration.
    /// </summary>
    /// <remarks>
    /// All data includes input and output data.
    /// </remarks>
    public bool GetAllData { get; set; }

    /// <summary>
    /// Gets or sets the name of the client to retrieve the orchestration from, if available.
    /// </summary>
    public string? ClientName { get; set; }

    /// <summary>
    /// Gets or sets an explicit <see cref="DurableTaskClient"/> to use to retrieve the orchestration status.
    /// </summary>
    public DurableTaskClient? Client { get; set; }
}
