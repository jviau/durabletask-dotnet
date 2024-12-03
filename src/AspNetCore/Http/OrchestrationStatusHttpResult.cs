// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Http;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.AspNetCore.Http;

/// <summary>
/// HTTP result that fetches and returns the status of an orchestration.
/// </summary>
/// <remarks>
/// Produces <see cref="StatusCodes.Status200OK"/> if found, <see cref="StatusCodes.Status404NotFound"/> otherwise.
/// </remarks>
public sealed partial class OrchestrationStatusHttpResult(string instanceId)
    : IResult, IOrchestrationStatusResult
{
    /// <summary>
    /// Gets the instance ID of the orchestration.
    /// </summary>
    public string InstanceId { get; } = instanceId ?? throw new ArgumentNullException(nameof(instanceId));

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

    /// <inheritdoc/>
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        DurableTaskClient client = this.GetClient(httpContext.RequestServices);
        OrchestrationMetadata? metadata = await client.GetInstanceAsync(
            this.InstanceId, this.GetAllData, httpContext.RequestAborted);

        ILogger logger = httpContext.RequestServices.GetRequiredService<ILogger<OrchestrationStatusHttpResult>>();
        if (metadata is null)
        {
            logger.OrchestrationNotFound(this.InstanceId);
            await Results.NotFound().ExecuteAsync(httpContext);
            return;
        }

        DurableAspNetCoreOptions options = httpContext.RequestServices
            .GetRequiredService<IOptions<DurableAspNetCoreOptions>>().Value;
        if (options.OperationLocation is { } behavior)
        {
            behavior.SetHeader(httpContext, this.InstanceId, logger);
        }

        await Results.Ok(metadata).ExecuteAsync(httpContext);
    }
}
