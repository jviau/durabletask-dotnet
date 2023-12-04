// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// A channel reader for <see cref="WorkMessage"/>.
/// </summary>
abstract class WorkDispatchReader : ChannelReader<WorkMessage>, IAsyncDisposable
{
    /// <inheritdoc/>
    public abstract ValueTask DisposeAsync();
}
