// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.DurableTask.AspNetCore.Http;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.AspNetCore.Mvc;

/// <summary>
/// An <see cref="ActionResult"/> that represents the status of an orchestration.
/// </summary>
[DefaultStatusCode(DefaultStatusCode)]
public class OrchestrationStatusResult : IOrchestrationStatusResult, IActionResult
{
    const int DefaultStatusCode = StatusCodes.Status200OK;

    MediaTypeCollection contentTypes;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestrationStatusResult"/> class.
    /// </summary>
    /// <param name="instanceId">The instance of the orchestration to provide status for.</param>
    public OrchestrationStatusResult(string instanceId)
    {
        ArgumentNullException.ThrowIfNull(instanceId);
        this.InstanceId = instanceId;
        this.Formatters = [];
        this.contentTypes = [];
    }

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

    /// <summary>
    /// Gets or sets the <see cref="FormatterCollection{IOutputFormatter}"/>.
    /// </summary>
    public FormatterCollection<IOutputFormatter> Formatters { get; set; }

    /// <summary>
    /// Gets or sets the <see cref="MediaTypeCollection"/>.
    /// </summary>
    public MediaTypeCollection ContentTypes
    {
        get => this.contentTypes;
        set => this.contentTypes = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <inheritdoc />
    Task IActionResult.ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        IActionResultExecutor<OrchestrationStatusResult> executor = context.HttpContext.RequestServices
            .GetRequiredService<IActionResultExecutor<OrchestrationStatusResult>>();
        return executor.ExecuteAsync(context, this);
    }
}
