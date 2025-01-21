// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using McMaster.Extensions.CommandLineUtils;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Extensions.CosmosDb;

namespace Preview.MediatorPattern;

[Command(Description = "Runs the cosmos mediator sample")]
public class CreateVmCommand : SampleCommandBase
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
