// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Runner for <see cref="OrchestrationWorkItem" />.
/// </summary>
partial class OrchestrationRunner : WorkItemRunner<OrchestrationWorkItem>
{
    readonly IServiceProvider services;
    readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestrationRunner"/> class.
    /// </summary>
    /// <param name="options">The options for this runner.</param>
    /// <param name="services">The service provider.</param>
    /// <param name="logger">The logger.</param>
    public OrchestrationRunner(
        WorkItemRunnerOptions options, IServiceProvider services, ILogger<OrchestrationRunner> logger)
        : base(options)
    {
        this.services = Check.NotNull(services);
        this.logger = Check.NotNull(logger);
    }

    /// <inheritdoc/>
    protected override async ValueTask RunAsync(
        OrchestrationWorkItem workItem, CancellationToken cancellation = default)
    {
        Check.NotNull(workItem);

        if (workItem.IsCompleted)
        {
            throw new InvalidOperationException("WorkItem already completed.");
        }

        await using AsyncServiceScope scope = this.services.CreateAsyncScope();
        if (!this.Factory.TryCreateOrchestrator(
            workItem.Name, scope.ServiceProvider, out ITaskOrchestrator? orchestrator))
        {
            throw new InvalidOperationException($"Orchestration {workItem.Name} does not exist.");
        }

        SynchronizationContext previous = SynchronizationContext.Current;
        try
        {
            OrchestrationSynchronizationContext context = new();
            SynchronizationContext.SetSynchronizationContext(context);
            Cursor cursor = new(workItem, this.Converter, orchestrator, this.logger);
            await cursor.RunAsync();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}
