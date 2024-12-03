// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.DurableTask.AspNetCore.Http;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.AspNetCore.Mvc;

/// <summary>
/// Executes an <see cref="OrchestrationStatusResult"/> to write the response.
/// </summary>
public partial class OrchestrationStatusResultExecutor : IActionResultExecutor<OrchestrationStatusResult>
{
    readonly ILogger logger;
    readonly DurableAspNetCoreOptions options;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestrationStatusResultExecutor"/> class.
    /// </summary>
    /// <param name="options">The durable mvc options.</param>
    /// <param name="logger">The logger.</param>
    public OrchestrationStatusResultExecutor(
        IOptions<DurableAspNetCoreOptions> options,
        ILogger<OrchestrationStatusResultExecutor> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        this.logger = logger;
        this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(ActionContext context, OrchestrationStatusResult result)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(result);

        DurableTaskClient client = result.GetClient(context.HttpContext.RequestServices);
        OrchestrationMetadata? metadata = await client.GetInstanceAsync(
            result.InstanceId, result.GetAllData, context.HttpContext.RequestAborted);

        if (metadata == null)
        {
            this.logger.OrchestrationNotFound(result.InstanceId);
            await new NotFoundResult().ExecuteResultAsync(context);
            return;
        }

        this.options.OperationLocationBehavior?.SetHeader(
            context.HttpContext, result.InstanceId, this.logger);
        ObjectResult objectResult = new(metadata)
        {
            Formatters = result.Formatters,
            ContentTypes = result.ContentTypes,
        };

        await objectResult.ExecuteResultAsync(context);
    }
}
