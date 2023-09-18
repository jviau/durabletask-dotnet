// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// A channel reader for <see cref="WorkDispatch"/>.
/// </summary>
abstract class WorkDispatchReader : ChannelReader<WorkDispatch>, IAsyncDisposable
{
    /// <inheritdoc/>
    public abstract ValueTask DisposeAsync();
}
