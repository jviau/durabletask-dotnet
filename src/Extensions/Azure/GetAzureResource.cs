// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Models;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.Azure;

namespace Microsoft.DurableTask.Extensions.Azure;

/// <summary>
/// An <see cref="IActivityRequest{TResourceData}" /> to get an Azure management resource.
/// </summary>
/// <typeparam name="TResourceData">The type of the resource to get.</typeparam>
public class GetAzureResource<TResourceData> : ResourceRequest, IActivityRequest<TResourceData>
    where TResourceData : ResourceData
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GetAzureResource{TResourceData}"/> class.
    /// </summary>
    /// <param name="id">The ID of the resource to get.</param>
    /// <param name="clientName">The name of the client to get.</param>
    public GetAzureResource(ResourceIdentifier id, string? clientName = null)
        : base(id, clientName)
    {
    }

    /// <inheritdoc/>
    public TaskName GetTaskName() => nameof(GetAzureResourceActivity);
}

/// <summary>
/// Internal implementation detail type, do not use directly.
/// </summary>
public class ResourceRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceRequest"/> class.
    /// </summary>
    /// <param name="id">The ID of the resource to get.</param>
    /// <param name="clientName">The name of the client to get.</param>
    public ResourceRequest(ResourceIdentifier id, string? clientName = null)
    {
        this.Id = id;
        this.ClientName = clientName ?? string.Empty;
    }

    /// <summary>
    /// Gets the ID of the resource to retrieve.
    /// </summary>
    public ResourceIdentifier Id { get; }

    /// <summary>
    /// Gets the name of the <see cref="ArmClient" /> to create via <see cref="IAzureClientFactory{ArmClient}" />.
    /// </summary>
    public string ClientName { get; }
}

/// <summary>
/// TaskActivity to get a management resource from Azure.
/// </summary>
/// <remarks>
/// This implementation relies on the serialization interoperability between <see cref="GenericResourceData" /> and the
/// desired resource described in <see cref="GetAzureResource{TResourceData}" />.
/// </remarks>
public class GetAzureResourceActivity : TaskActivity<ResourceRequest, GenericResourceData>
{
    readonly IAzureClientFactory<ArmClient> clientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetAzureResourceActivity"/> class.
    /// </summary>
    /// <param name="clientFactory">The Azure client factory.</param>
    public GetAzureResourceActivity(IAzureClientFactory<ArmClient> clientFactory)
    {
        this.clientFactory = Check.NotNull(clientFactory);
    }

    /// <inheritdoc/>
    public override async Task<GenericResourceData> RunAsync(TaskActivityContext context, ResourceRequest input)
    {
        Check.NotNull(input);
        ArmClient client = this.clientFactory.CreateClient(input.ClientName);
        GenericResource resource = client.GetGenericResource(input.Id);
        resource = await resource.GetAsync();
        return resource.Data;
    }
}
