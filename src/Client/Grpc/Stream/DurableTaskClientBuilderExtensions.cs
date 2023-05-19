// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Client.Grpc;
using Microsoft.Extensions.DependencyInjection;
using S = Microsoft.DurableTask.Client.Grpc.Stream;

namespace Microsoft.DurableTask.Client;

/// <summary>
/// Extension methods for adding Durable Task support to .NET hosted services, such as ASP.NET Core hosts.
/// </summary>
public static class GrpcDurableTaskClientBuilderExtensions
{
    /// <summary>
    /// Configures the <see cref="IDurableTaskClientBuilder" /> to be a gRPC client.
    /// </summary>
    /// <remarks>
    /// This must be called independently of worker registration.
    /// </remarks>
    /// <param name="builder">The builder to configure.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskClientBuilder UseStreamGrpc(this IDurableTaskClientBuilder builder)
        => builder.UseStreamGrpc(opt => { });

    /// <summary>
    /// Configures the <see cref="IDurableTaskClientBuilder" /> to be a gRPC client.
    /// </summary>
    /// <remarks>
    /// This must be called independently of worker registration.
    /// </remarks>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="address">The address of the Durable Task sidecar endpoint.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskClientBuilder UseStreamGrpc(this IDurableTaskClientBuilder builder, string address)
        => builder.UseStreamGrpc(opt => opt.Address = address);

    /// <summary>
    /// Configures the <see cref="IDurableTaskClientBuilder" /> to be a gRPC client.
    /// </summary>
    /// <remarks>
    /// This must be called independently of worker registration.
    /// </remarks>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="channel">The channel for the Durable Task sidecar endpoint.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskClientBuilder UseStreamGrpc(this IDurableTaskClientBuilder builder, GrpcChannel channel)
        => builder.UseStreamGrpc(opt => opt.Channel = channel);

    /// <summary>
    /// Configures the <see cref="IDurableTaskClientBuilder" /> to be a gRPC client.
    /// </summary>
    /// <remarks>
    /// This must be called independently of worker registration.
    /// </remarks>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="configure">The action to configure the gRPC options.</param>
    /// <returns>The original builder, for call chaining.</returns>
    public static IDurableTaskClientBuilder UseStreamGrpc(
        this IDurableTaskClientBuilder builder, Action<GrpcDurableTaskClientOptions> configure)
    {
        Check.NotNull(builder);
        Check.NotNull(configure);
        builder.Services.Configure(builder.Name, configure);
        return builder.UseBuildTarget<S.GrpcDurableTaskClient, GrpcDurableTaskClientOptions>();
    }
}
