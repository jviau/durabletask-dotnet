// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Runner for <see cref="OrchestrationWorkItem" />.
/// </summary>
partial class OrchestrationRunner : WorkItemRunner<OrchestrationWorkItem, OrchestrationRunnerOptions>
{
    readonly IServiceProvider services;
    readonly ILoggerFactory loggerFactory;
    readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrchestrationRunner"/> class.
    /// </summary>
    /// <param name="options">The options for this runner.</param>
    /// <param name="services">The service provider.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public OrchestrationRunner(
        OrchestrationRunnerOptions options, IServiceProvider services, ILoggerFactory loggerFactory)
        : base(options)
    {
        this.services = Check.NotNull(services);
        this.loggerFactory = Check.NotNull(loggerFactory);
        this.logger = loggerFactory.CreateLogger<OrchestrationRunner>();
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

        ITaskOrchestrator? orchestrator;
        await using AsyncServiceScope scope = this.services.CreateAsyncScope();

        try
        {
            if (!this.Factory.TryCreateOrchestrator(workItem.Name, scope.ServiceProvider, out orchestrator))
            {
                throw new TaskMissingException(workItem.Name, typeof(ITaskOrchestrator));
            }
        }
        catch (TaskMissingException ex)
        {
            ExecutionCompleted completed = new(-1, DateTimeOffset.UtcNow, null, TaskFailureDetails.FromException(ex));
            await workItem.Channel.Writer.WriteAsync(completed, cancellation);
            await workItem.ReleaseAsync(cancellation);
            throw;
        }

        try
        {
            await workItem.InitializeAsync(cancellation);
            Cursor cursor = new(workItem, this.Options, orchestrator, this.loggerFactory, cancellation);
            await cursor.RunAsync();
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Error running orchestration {InstanceId}", workItem.Id);
        }
        finally
        {
            await workItem.ReleaseAsync(cancellation);
        }
    }
}
