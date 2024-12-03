// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Http;
using Microsoft.DurableTask.Client;

namespace Microsoft.DurableTask.AspNetCore;

/// <summary>
/// Options for configuring Durable AspNetCore integrations.
/// </summary>
public class DurableAspNetCoreOptions
{
    static readonly Func<HttpContext, OrchestrationMetadata, object> DefaultConvertMetadata = (_, m) => m;

    /// <summary>
    /// Gets or sets the behavior for operation location.
    /// </summary>
    public OperationLocationBehavior? OperationLocationBehavior { get; set; }
}
