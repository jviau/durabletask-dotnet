// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using DurableTask.Core;
using DurableTask.DependencyInjection;
using DurableTask.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.DurableTask.Prototype;

static partial class BaselineRunner
{
    class CoreRunner : Runner
    {
        TaskHubClient client = null!;
        IOrchestrationServicePurgeClient purgeClient = null!;
        readonly BaselineOptions options;

        public CoreRunner(BaselineOptions options)
        {
            this.options = options;
        }

        protected override string Description => this.options.Description;

        protected override Task<IHost> CreateHostAsync()
        {
            Console.WriteLine("Running baseline benchmark");
            IHost host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddOrchestrationService("baseline");
                })
                .ConfigureTaskHubWorker(builder =>
                {
                    builder.AddActivity<TestCoreActivity>();
                    builder.AddOrchestration<TestCoreOrchestration>();

                    builder.AddActivity<FibEndCoreActivity>();
                    builder.AddOrchestration<FibCoreOrchestration>();
                    builder.AddClient();
                },
                opt => opt.CreateIfNotExists = true)
                .Build();

            this.client = host.Services.GetRequiredService<TaskHubClient>();
            this.purgeClient = (IOrchestrationServicePurgeClient)host.Services
                .GetRequiredService<IOrchestrationServiceClient>();

            return Task.FromResult(host);
        }

        protected override ValueTask DisposeCoreAsync() => default;

        protected override Task IterationCleanupAsync(CancellationToken cancellation)
        {
            OrchestrationStatus[] statuses = new[]
            {
                OrchestrationStatus.Completed,
                OrchestrationStatus.Failed,
                OrchestrationStatus.Pending,
                OrchestrationStatus.Suspended,
                OrchestrationStatus.Running,
                OrchestrationStatus.Canceled,
                OrchestrationStatus.ContinuedAsNew,
                OrchestrationStatus.Terminated,
            };

            PurgeInstanceFilter filter = new(createdTimeFrom: DateTime.MinValue, null, statuses);
            return this.purgeClient.PurgeInstanceStateAsync(filter);
        }

        protected override async Task RunIterationAsync(bool isWarmup, CancellationToken cancellation)
        {
            int depth = isWarmup ? 2 : this.options.Depth;

            int fib = Fib.Get(depth);
            async Task RunOrchestrationAsync(int depth)
            {
                await Task.Yield();
                OrchestrationInstance instance = await this.client.CreateOrchestrationInstanceAsync(
                    typeof(FibCoreOrchestration), depth);
                OrchestrationState state = await this.client.WaitForOrchestrationAsync(
                    instance, TimeSpan.MaxValue, cancellation);
                if (state.OrchestrationStatus != OrchestrationStatus.Completed)
                {
                    throw new InvalidOperationException($"Unexpected status of {state.OrchestrationStatus}.");
                }

                int output = JsonSerializer.Deserialize<int>(state.Output);
                if (output != fib)
                {
                    throw new InvalidOperationException($"Incorrect result: expected {fib}, actual {output}.");
                }
            }

            Task[] tasks = new Task[this.options.Count];
            for (int i = 0; i < this.options.Count; i++)
            {
                tasks[i] = RunOrchestrationAsync(depth);
            }

            await Task.WhenAll(tasks).WaitAsync(cancellation);
        }
    }
}
