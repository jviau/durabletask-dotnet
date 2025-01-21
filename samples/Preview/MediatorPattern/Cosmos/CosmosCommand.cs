// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using McMaster.Extensions.CommandLineUtils;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Extensions.CosmosDb;

namespace Preview.MediatorPattern.Cosmos;

[Command(Description = "Runs the cosmos mediator sample")]
public class Cosmos1Command : SampleCommandBase
{
    public static void Register(DurableTaskRegistry tasks)
    {
        tasks.AddQueryContainerActivity<Document>();
        tasks.AddOrchestrator<CosmosOrchestrator>();
    }

    protected override IBaseOrchestrationRequest GetRequest()
    {
        return CosmosOrchestrator.CreateRequest();
    }
}

[Command(Description = "Runs the cosmos aggregation mediator sample")]
public class Cosmos2Command : SampleCommandBase
{
    public static void Register(DurableTaskRegistry tasks)
    {
        tasks.AddOrchestrator<CosmosAggregationOrchestrator>();
    }

    protected override IBaseOrchestrationRequest GetRequest()
    {
        return CosmosAggregationOrchestrator.CreateRequest();
    }
}
