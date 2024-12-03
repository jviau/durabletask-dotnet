// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.DurableTask.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.DurableTask.AspNetCore;

/// <summary>
/// Extensions for <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Durable AspNetCore integration.
    /// </summary>
    /// <param name="services">The services to configure.</param>
    /// <returns>The service collection for call chaining.</returns>
    public static IServiceCollection AddDurableClientAspNetCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<DurableAspNetCoreOptions>();
        services.TryAddSingleton<
            IActionResultExecutor<OrchestrationStatusResult>, OrchestrationStatusResultExecutor>();

        return services;
    }
}
