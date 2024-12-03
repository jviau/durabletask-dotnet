// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.AspNetCore;

/// <summary>
/// Options for configuring operation location behavior.
/// </summary>
/// <param name="RouteName">The named route endpoint used to get operation location status.</param>
public record OperationLocationBehavior(string RouteName)
{
    /// <summary>
    /// Gets the name of the header to set the operation location to.
    /// </summary>
    public string HeaderName { get; init; } = "Operation-Location";

    /// <summary>
    /// Gets the name of the route parameter for the instance ID.
    /// </summary>
    public string ParameterName { get; init; } = "instanceId";

    /// <summary>
    /// Sets the operation location header in the response.
    /// </summary>
    /// <param name="context">The http context.</param>
    /// <param name="instanceId">The orchestration instance ID.</param>
    /// <param name="logger">The logger.</param>
    internal void SetHeader(HttpContext context, string instanceId, ILogger logger)
    {
        LinkGenerator linkGenerator = context.RequestServices.GetRequiredService<LinkGenerator>();
        string? route = linkGenerator.GetUriByRouteValues(
            context,
            this.RouteName,
            new RouteValueDictionary { [this.ParameterName] = instanceId });

        if (route is null)
        {
            logger.CannotFindRoute(this.RouteName, this.ParameterName);
            throw new InvalidOperationException("No route matches the supplied values.");
        }

        context.Response.Headers.Append(this.HeaderName, route);
        logger.OperationLocationHeaderSet(route);
    }
}
