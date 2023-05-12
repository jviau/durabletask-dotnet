// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Worker.Hosting;

/// <summary>
/// The default durable task worker.
/// </summary>
public class ChannelDurableTaskWorker : DurableTaskWorker
{
    readonly ChannelDurableTaskWorkerOptions options;
    readonly IServiceProvider services;
    readonly Dictionary<Type, IWorkItemRunner> runners = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelDurableTaskWorker"/> class.
    /// </summary>
    /// <param name="name">The name of this worker.</param>
    /// <param name="factory">The durable task factory for this worker.</param>
    /// <param name="options">The worker options.</param>
    /// <param name="services">The service provider.</param>
    public ChannelDurableTaskWorker(
        string? name,
        IDurableTaskFactory factory,
        IOptionsMonitor<ChannelDurableTaskWorkerOptions> options,
        IServiceProvider services)
        : base(name, factory)
    {
        Check.NotNull(options);
        this.options = options.Get(name);
        this.services = Check.NotNull(services);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this.InitializeRunners();
        ChannelReader<WorkItem> reader = this.options.WorkItemReader;
        while (await reader.WaitToReadAsync(stoppingToken))
        {
            while (reader.TryRead(out WorkItem? item))
            {
                ThreadPool.QueueUserWorkItem(this.ProcessWorkItem, (item, stoppingToken));
            }
        }
    }

    static Type GetOptionType(Type runner)
    {
        while (runner is not null)
        {
            if (runner.IsGenericType && runner.GetGenericTypeDefinition() == typeof(WorkItemRunner<,>))
            {
                return runner.GetGenericArguments()[1];
            }

            runner = runner.BaseType;
        }

        throw new NotSupportedException();
    }

    void InitializeRunners()
    {
        foreach ((Type workItem, Type runner) in this.options.Runners)
        {
            Type optionsType = GetOptionType(runner);
            Type monitorType = typeof(IOptionsMonitor<>).MakeGenericType(optionsType);
            IOptionsMonitor<WorkItemRunnerOptions> monitor = (IOptionsMonitor<WorkItemRunnerOptions>)this.services
                .GetService(monitorType);
            WorkItemRunnerOptions options = monitor.Get(this.Name);
            options.Factory = this.Factory;
            this.runners[workItem] = (IWorkItemRunner)ActivatorUtilities.CreateInstance(this.services, runner, options);
        }
    }

    async void ProcessWorkItem(object state)
    {
        (WorkItem item, CancellationToken cancellation) = Check.IsType<ValueTuple<WorkItem, CancellationToken>>(state);
        IWorkItemRunner runner = this.GetRunner(item.GetType());
        await runner.RunAsync(item, cancellation);
    }

    IWorkItemRunner GetRunner(Type t)
    {
        while (t is not null)
        {
            if (this.runners.TryGetValue(t, out IWorkItemRunner runner))
            {
                return runner;
            }

            t = t.BaseType;
        }

        throw new NotSupportedException();
    }
}
