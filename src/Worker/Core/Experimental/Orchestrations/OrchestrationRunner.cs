// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Runner for <see cref="OrchestrationWorkItem" />.
/// </summary>
partial class OrchestrationRunner : IWorkItemRunner<OrchestrationWorkItem>
{
    readonly IServiceProvider services;
    readonly IDurableTaskFactory factory;
    readonly DataConverter converter;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestrationRunner"/> class.
    /// </summary>
    /// <param name="services">The service provider.</param>
    /// <param name="factory">The durable task factory.</param>
    /// <param name="converter">The data converter.</param>
    public OrchestrationRunner(IServiceProvider services, IDurableTaskFactory factory, DataConverter converter)
    {
        this.services = Check.NotNull(services);
        this.factory = Check.NotNull(factory);
        this.converter = Check.NotNull(converter);
    }

    /// <inheritdoc/>
    public async ValueTask RunAsync(OrchestrationWorkItem workItem, CancellationToken cancellation = default)
    {
        Check.NotNull(workItem);

        if (workItem.IsCompleted)
        {
            throw new InvalidOperationException("WorkItem already completed.");
        }

        await using AsyncServiceScope scope = this.services.CreateAsyncScope();
        if (!this.factory.TryCreateOrchestrator(
            workItem.Name, scope.ServiceProvider, out ITaskOrchestrator? orchestrator))
        {
            throw new InvalidOperationException($"Orchestration {workItem.Name} does not exist.");
        }

        SynchronizationContext previous = SynchronizationContext.Current;
        try
        {
            OrchestrationSynchronizationContext context = new();
            SynchronizationContext.SetSynchronizationContext(context);
            Cursor cursor = new(workItem, this.converter, orchestrator);
            await cursor.RunAsync();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}
