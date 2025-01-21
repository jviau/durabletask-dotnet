// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.ClientModel.Primitives;
using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;

namespace Microsoft.DurableTask.Extensions.Azure.ResourceManager;

/// <summary>
/// Helpers for ARM operations.
/// </summary>
static class ArmOperationHelpers
{
    /// <summary>
    /// Converts a <see cref="VirtualMachineResource"/> operation to one for <see cref="VirtualMachineData"/>.
    /// </summary>
    /// <param name="operation">The operation to convert.</param>
    /// <returns>The converted operation.</returns>
    public static ArmOperation<VirtualMachineData> AsDataOperation(this ArmOperation<VirtualMachineResource> operation)
        => operation.AsDataOperation(op => op.Value.Data);

    /// <summary>
    /// Gets a data operation from an ARM operation.
    /// </summary>
    /// <typeparam name="TResource">The resource type of the original operation.</typeparam>
    /// <typeparam name="TData">The data type.</typeparam>
    /// <param name="operation">The operation.</param>
    /// <param name="getData">The callback to get the data from the operation.</param>
    /// <returns>An operation shim to get the data.</returns>
    public static ArmOperation<TData> AsDataOperation<TResource, TData>(
        this ArmOperation<TResource> operation, Func<ArmOperation<TResource>, TData> getData)
        where TResource : ArmResource
        where TData : IPersistableModel<TData>
    {
        Check.NotNull(operation);
        Check.NotNull(getData);
        return new ArmDataOperation<TResource, TData>(operation, getData);
    }

    class ArmDataOperation<TResource, TData>(
        ArmOperation<TResource> operation, Func<ArmOperation<TResource>, TData> getData)
        : ArmOperation<TData>
        where TData : IPersistableModel<TData>
    {
        public override TData Value => getData(operation);

        public override bool HasValue => operation.HasValue;

        public override string Id => operation.Id;

        public override bool HasCompleted => operation.HasCompleted;

        public override RehydrationToken? GetRehydrationToken() => operation.GetRehydrationToken();

        public override Response GetRawResponse() => operation.GetRawResponse();

        public override Response UpdateStatus(CancellationToken cancellationToken = default)
            => operation.UpdateStatus(cancellationToken);

        public override ValueTask<Response> UpdateStatusAsync(CancellationToken cancellationToken = default)
            => operation.UpdateStatusAsync(cancellationToken);
    }
}
