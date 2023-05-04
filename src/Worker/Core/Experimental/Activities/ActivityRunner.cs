// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Runner for <see cref="ActivityWorkItem" />.
/// </summary>
class ActivityRunner : IWorkItemRunner<ActivityWorkItem>
{
    readonly IDurableTaskFactory factory;
    readonly DataConverter converter;
    readonly IServiceProvider services;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityRunner"/> class.
    /// </summary>
    /// <param name="factory">The durable factory.</param>
    /// <param name="converter">The data converter.</param>
    /// <param name="services">The service provider.</param>
    public ActivityRunner(IDurableTaskFactory factory, DataConverter converter, IServiceProvider services)
    {
        this.factory = Check.NotNull(factory);
        this.converter = Check.NotNull(converter);
        this.services = Check.NotNull(services);
    }

    /// <inheritdoc/>
    public async ValueTask RunAsync(ActivityWorkItem workItem, CancellationToken cancellation = default)
    {
        Check.NotNull(workItem);
        await using AsyncServiceScope scope = this.services.CreateAsyncScope();
        if (!this.factory.TryCreateActivity(workItem.Name, scope.ServiceProvider, out ITaskActivity? activity))
        {
            throw new InvalidOperationException($"ITaskActivity with name '{workItem.Name}' does not exist.");
        }

        object? input = this.converter.Deserialize(workItem.Input, activity.InputType);
        try
        {
            object? result = await activity.RunAsync(new Context(workItem), input);
            await workItem.CompleteAsync(this.converter.Serialize(result));
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

        public override string InstanceId => this.workItem.Parent.InstanceId;
    }
}
