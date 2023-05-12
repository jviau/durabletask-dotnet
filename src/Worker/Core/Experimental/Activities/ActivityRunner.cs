// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Runner for <see cref="ActivityWorkItem" />.
/// </summary>
class ActivityRunner : WorkItemRunner<ActivityWorkItem>
{
    readonly IServiceProvider services;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityRunner"/> class.
    /// </summary>
    /// <param name="options">The options for this runner.</param>
    /// <param name="services">The service provider.</param>
    public ActivityRunner(WorkItemRunnerOptions options, IServiceProvider services)
        : base(options)
    {
        this.services = Check.NotNull(services);
    }

    /// <inheritdoc/>
    protected override async ValueTask RunAsync(ActivityWorkItem workItem, CancellationToken cancellation = default)
    {
        Check.NotNull(workItem);
        await using AsyncServiceScope scope = this.services.CreateAsyncScope();
        if (!this.Factory.TryCreateActivity(workItem.Name, scope.ServiceProvider, out ITaskActivity? activity))
        {
            throw new InvalidOperationException($"{nameof(ITaskActivity)} with name '{workItem.Name}' does not exist.");
        }

        object? input = this.Converter.Deserialize(workItem.Input, activity.InputType);
        try
        {
            object? result = await activity.RunAsync(new Context(workItem), input);
            await workItem.CompleteAsync(this.Converter.Serialize(result));
        }
        catch (Exception ex)
        {
            // TODO: check if fatal.
            await workItem.FailAsync(ex);
        }
    }

    class Context : TaskActivityContext
    {
        readonly ActivityWorkItem workItem;

        public Context(ActivityWorkItem workItem)
        {
            this.workItem = Check.NotNull(workItem);
        }

        public override TaskName Name => this.workItem.Name;

        public override string InstanceId => this.workItem.Id;
    }
}
