// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.AzureStorage;
using DurableTask.Core;
using Microsoft.DurableTask.Grpc.Hub;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Benchmarks.EndToEnd;

static class OrchestrationService
{
    public enum Kind
    {
        InMemory = 0,
        AzureStorage = 1,
    }

    public class Options
    {
        public string? Name { get; set; }

        public Kind Kind { get; set; }

        public bool UseSessions { get; set; }

        public string? ConnectionString { get; set; }

        public ILoggerFactory? LoggerFactory { get; set; }

        public static Options Default(string name) => new() { Name = name, Kind = Kind.InMemory };
    }

    internal static AzureStorageOrchestrationService CreateAzureStorage(Options options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Console.WriteLine($"Using AzureStorage: sessions={options.UseSessions}");
        Console.WriteLine($"Development Storage: {options.ConnectionString is null}");
        AzureStorageOrchestrationServiceSettings settings = new()
        {
            PartitionCount = 4,
            MaxConcurrentTaskActivityWorkItems = int.MaxValue,
            MaxConcurrentTaskOrchestrationWorkItems = int.MaxValue,
            StorageConnectionString = options.ConnectionString ?? "UseDevelopmentStorage=true",
            LoggerFactory = options.LoggerFactory ?? NullLoggerFactory.Instance,
            TaskHubName = options.Name ?? "prototype",
            ExtendedSessionsEnabled = options.UseSessions,
        };

        return new AzureStorageOrchestrationService(settings);
    }

    internal static InMemoryOrchestrationService CreateInMemory(Options options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new InMemoryOrchestrationService(options.UseSessions);
    }

    internal static IOrchestrationService Create(Options options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Kind switch
        {
            Kind.InMemory => CreateInMemory(options),
            Kind.AzureStorage => CreateAzureStorage(options),
            _ => throw new NotSupportedException(),
        };
    }

    internal static IServiceCollection AddOrchestrationService(this IServiceCollection services, Options options)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(sp =>
        {
            if (options.LoggerFactory is null)
            {
                ILoggerFactory lf = sp.GetRequiredService<ILoggerFactory>();
                options = new()
                {
                    Name = options.Name,
                    Kind = options.Kind,
                    LoggerFactory = lf,
                    UseSessions = options.UseSessions,
                    ConnectionString = options.ConnectionString,
                };
            }

            return Create(options);
        });

        services.AddSingleton(sp => (IOrchestrationServiceClient)sp.GetRequiredService<IOrchestrationService>());
        return services;
    }

    internal static IServiceCollection AddOrchestrationService(this IServiceCollection services, string name)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<Options>()
            .BindConfiguration("OrchestrationService")
            .Configure<ILoggerFactory>((o, lf) =>
            {
                o.Name = name;
                o.LoggerFactory = lf;
            });

        services.AddSingleton(sp =>
        {
            IOptions<Options> options = sp.GetRequiredService<IOptions<Options>>();
            return Create(options.Value);
        });

        services.AddSingleton(sp => (IOrchestrationServiceClient)sp.GetRequiredService<IOrchestrationService>());
        return services;
    }
}
