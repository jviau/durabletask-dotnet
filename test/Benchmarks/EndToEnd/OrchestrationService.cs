﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.AzureStorage;
using DurableTask.Core;
using Microsoft.DurableTask.Grpc.Hub;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DurableTask.Benchmarks.EndToEnd;

static class OrchestrationService
{
    public abstract record Kind(string Name, ILoggerFactory? LoggerFactory)
    {
        public static Kind Default(string name, ILoggerFactory? loggerFactory = null)
            => InMemory(name, loggerFactory, useSessions: true);

        public static Kind AzureStorage(string name, ILoggerFactory? loggerFactory = null)
            => new AzureStorageKind(name, loggerFactory);

        public static Kind InMemory(string name, ILoggerFactory? loggerFactory = null, bool useSessions = false)
            => new InMemoryKind(name, loggerFactory, useSessions);
    }

    internal record AzureStorageKind(string Name, ILoggerFactory? LoggerFactory) : Kind(Name, LoggerFactory);

    internal record InMemoryKind(string Name, ILoggerFactory? LoggerFactory, bool UseSessions)
        : Kind(Name, LoggerFactory);

    internal static AzureStorageOrchestrationService CreateAzureStorage(
        string name, ILoggerFactory? loggerFactory = null)
    {
        loggerFactory ??= NullLoggerFactory.Instance;
        AzureStorageOrchestrationServiceSettings settings = new()
        {
            PartitionCount = 1,
            StorageConnectionString = "UseDevelopmentStorage=true",
            LoggerFactory = loggerFactory,
            TaskHubName = $"prototype{name}",
        };

        return new AzureStorageOrchestrationService(settings);
    }

    internal static InMemoryOrchestrationService CreateInMemory(bool useSessions = false)
    {
        return new InMemoryOrchestrationService(useSessions);
    }

    internal static IOrchestrationService Create(Kind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        return kind switch
        {
            InMemoryKind k => CreateInMemory(k.UseSessions),
            AzureStorageKind k => CreateAzureStorage(k.Name, k.LoggerFactory),
            _ => throw new NotSupportedException(),
        };
    }

    internal static IServiceCollection AddOrchestrationService(this IServiceCollection services, Kind kind)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(sp =>
        {
            if (kind.LoggerFactory is null)
            {
                ILoggerFactory lf = sp.GetRequiredService<ILoggerFactory>();
                kind = kind with { LoggerFactory = lf };
            }

            return Create(kind);
        });
        services.AddSingleton(sp => (IOrchestrationServiceClient)sp.GetRequiredService<IOrchestrationService>());
        return services;
    }
}
