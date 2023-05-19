// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.DurableTask.Grpc.Hub;

/// <summary>
/// Extensions for <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the TaskHub gRPC services to the <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The service collection to add.</param>
    /// <returns><paramref name="services"/> with services added.</returns>
    public static IServiceCollection AddTaskHubGrpc(this IServiceCollection services)
    {
        Check.NotNull(services);
        services.TryAddSingleton<GrpcTaskHubServer>();
        services.TryAddSingleton<GrpcTaskClientServer>();
        services.TryAddSingleton<InMemoryInstanceStore>();
        services.TryAddSingleton<InMemoryOrchestrationService>();
        services.TryAddSingleton<IOrchestrationService>(sp => sp.GetRequiredService<InMemoryOrchestrationService>());
        services.TryAddSingleton<IOrchestrationServiceClient>(sp => sp.GetRequiredService<InMemoryOrchestrationService>());
        return services;
    }
}
