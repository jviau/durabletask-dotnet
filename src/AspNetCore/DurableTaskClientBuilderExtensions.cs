// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.AspNetCore;

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Extensions for <see cref="IDurableTaskClientBuilder"/>.
/// </summary>
public static class DurableTaskClientBuilderExtensions
{
    /// <summary>
    /// Adds durable AspNetCore integration.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <returns>The builder for call chaining.</returns>
    public static IDurableTaskClientBuilder AddAspNetCore(this IDurableTaskClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddDurableClientAspNetCore();
        return builder;
    }
}
