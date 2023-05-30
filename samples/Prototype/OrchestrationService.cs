// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using DurableTask.AzureStorage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.DurableTask.Prototype;

static class OrchestrationService
{
    internal static AzureStorageOrchestrationService CreateAzureStorage(string name, ILoggerFactory? loggerFactory = null)
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
}
