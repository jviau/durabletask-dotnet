// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.AspNetCore;

/// <summary>
/// Options for configuring Durable AspNetCore integrations.
/// </summary>
public class DurableAspNetCoreOptions
{
    /// <summary>
    /// Gets or sets the behavior for operation location.
    /// </summary>
    public OperationLocationBehavior? OperationLocation { get; set; }
}
