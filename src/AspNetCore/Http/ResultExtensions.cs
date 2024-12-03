// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.AspNetCore.Http;

namespace Microsoft.AspNetCore.Http;

/// <summary>
/// Extensions for <see cref="IResultExtensions"/>.
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// An <see cref="OrchestrationStatusHttpResult"/> that returns the status of an orchestration instance.
    /// </summary>
    /// <param name="ext">The result extensions. Discard.</param>
    /// <param name="instanceId">The instance id.</param>
    /// <returns>The orchestration status result.</returns>
    public static OrchestrationStatusHttpResult OrchestrationStatus(this IResultExtensions ext, string instanceId)
    {
        ArgumentNullException.ThrowIfNull(instanceId);
        return new(instanceId);
    }
}
