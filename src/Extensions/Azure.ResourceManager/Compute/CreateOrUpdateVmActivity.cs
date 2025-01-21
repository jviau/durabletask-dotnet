// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.Azure;

namespace Microsoft.DurableTask.Extensions.Azure.ResourceManager.Compute;

/// <summary>
/// A request to create a virtual machine.
/// </summary>
/// <param name="clientName">The name of the <see cref="ArmClient"/> to use.</param>
/// <param name="id">The virtual machine resource ID to create.</param>
/// <param name="data">The data of the VM to create or update.</param>
public sealed class CreateOrUpdateVmActivityRequest(
    string clientName, ResourceIdentifier id, VirtualMachineData data)
    : ArmLroRequest<VirtualMachineData>
{
    /// <summary>
    /// Registers this task to the <see cref="DurableTaskRegistry"/>.
    /// </summary>
    /// <param name="registry">The registry to use.</param>
    internal static void Register(DurableTaskRegistry registry)
    {
        Check.NotNull(registry);
        registry.AddActivity<Handler>(nameof(CreateOrUpdateVmActivityRequest));
    }

    /// <inheritdoc/>
    protected override IArmLroStartReqeust<VirtualMachineData> GetStartRequestCore() => new Request(clientName, id, data);

    sealed class Handler(IAzureClientFactory<ArmClient> clients) : ArmStartLroActivity<Request, VirtualMachineData>
    {
        protected override async Task<ArmOperation<VirtualMachineData>> BeginOperationAsync(TaskActivityContext context, Request input)
        {
            Check.NotNull(input);

            ArmClient client = clients.CreateClient(input.ClientName);
            ResourceIdentifier rgId = ResourceGroupResource.CreateResourceIdentifier(input.Id.SubscriptionId, input.Id.ResourceGroupName);
            ResourceGroupResource rg = client.GetResourceGroupResource(rgId);
            ArmOperation<VirtualMachineResource> op = await rg.GetVirtualMachines().CreateOrUpdateAsync(WaitUntil.Started, input.Id.Name, input.Data);
            return op.AsDataOperation();
        }
    }

    class Request(string clientName, ResourceIdentifier id, VirtualMachineData data) : CreateOrUpdateResourceRequest<VirtualMachineData>(clientName, id)
    {
        public VirtualMachineData Data { get; } = data;

        public override TaskName GetTaskName() => nameof(CreateOrUpdateVmActivityRequest);
    }
}
