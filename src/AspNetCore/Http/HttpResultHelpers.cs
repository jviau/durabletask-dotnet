// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.AspNetCore.Http;

/// <summary>
/// Helpers for http results.
/// </summary>
static class HttpResultHelpers
{
    /// <summary>
    /// Gets the client to use for a <see cref="IOrchestrationStatusResult"/>.
    /// </summary>
    /// <param name="result">The result to get the client for.</param>
    /// <param name="services">The service provider.</param>
    /// <returns>The <see cref="DurableTaskClient"/> to use.</returns>
    public static DurableTaskClient GetClient(
        this IOrchestrationStatusResult result, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(services);

        if (result.Client is not null)
        {
            return result.Client;
        }

        string clientName = result.ClientName ?? string.Empty;
        IDurableTaskClientProvider clientProvider = services.GetRequiredService<IDurableTaskClientProvider>();
        return clientProvider.GetClient(clientName);
    }
}
