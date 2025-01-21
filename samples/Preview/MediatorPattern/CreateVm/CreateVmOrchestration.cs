// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using Azure.ResourceManager.Compute;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Extensions.Azure.ResourceManager.Compute;

namespace Preview.MediatorPattern.CreateVm;

/**
* This sample shows how mediator-pattern orchestrations and activities can be leveraged for advanced scenarios
* with seemless consumption. In this case, we are enqueueing a 'single' activity which will actually be
* multiple activities, correctly paging the results of a CosmosDB query to the parent orchestration.
*
* This uses IAsyncEnumerable{T}, so all behaviors with that enumerable work as expected.
*/

public static class ArmConstants
{
    public const string Subscription = "some-subscription";
    public const string ClientName = "some-arm-client";
}

/// <summary>
/// This orchestration creates a VM using the Azure SDK for .NET. While it appears we are enqueing a single activity,
/// the "RunAsync" call used will actually wrap that activity request in an orchestration to handle the long-running
/// operation of creating a VM.
/// </summary>
public class CreateVmOrchestrator : TaskOrchestrator
{
    public static IOrchestrationRequest CreateRequest() => OrchestrationRequest.Create(nameof(CreateVmOrchestrator));

    public override async Task RunAsync(TaskOrchestrationContext context)
    {
        // Some data filled out..
        VirtualMachineData data = new(AzureLocation.CentralUS);
        ResourceIdentifier id = VirtualMachineResource.CreateResourceIdentifier("my-subscription", "my-rg", "my-vm");

        await context.RunAsync(new CreateOrUpdateVmActivityRequest(ArmConstants.ClientName, id, data));
    }
}
