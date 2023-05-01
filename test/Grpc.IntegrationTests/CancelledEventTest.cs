// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Worker;
using Nito.AsyncEx;
using Xunit.Abstractions;

namespace Microsoft.DurableTask.Grpc.Tests;

public class CancelledEventTest : IntegrationTestBase
{
    readonly AsyncAutoResetEvent reset = new();

    public CancelledEventTest(ITestOutputHelper output, GrpcSidecarFixture sidecarFixture)
        : base(output, sidecarFixture)
    { }

    [Fact]
    public async Task EventIssue()
    {
        TaskName name = nameof(Orchestrator);
        await using HostTestLifetime server = await this.StartWorkerAsync(b =>
        {
            b.AddTasks(tasks => tasks.AddOrchestratorFunc(name, this.Orchestrator));
        });

        string instanceId = await server.Client.ScheduleNewOrchestrationInstanceAsync(name);
        await this.reset.WaitAsync();
        await server.Client.RaiseEventAsync(instanceId, "One");

        await this.reset.WaitAsync();
        await server.Client.RaiseEventAsync(instanceId, "Recurring");

        await server.Client.WaitForInstanceCompletionAsync(instanceId);
    }

    async Task Orchestrator(TaskOrchestrationContext context)
    {
        using (CancellationTokenSource cts = new())
        {
            Task event1 = context.WaitForExternalEvent<object>("Recurring", cts.Token);
            this.reset.Set();
            await context.WaitForExternalEvent<object>("One");
            cts.Cancel();
        }

        this.reset.Set();
        await context.WaitForExternalEvent<object>("Recurring");
    }
}
