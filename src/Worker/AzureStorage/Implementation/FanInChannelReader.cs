// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

namespace Microsoft.DurableTask.Worker.AzureStorage.Implementation;

/// <summary>
/// Helpers for fanning-in multiple <see cref="ChannelReader{T}"/> results.
/// </summary>
static class FanInChannelReader
{
    /// <summary>
    /// Creates a new channel reader which will fan in the results of both channels.
    /// </summary>
    /// <typeparam name="T">The type held by the channels.</typeparam>
    /// <param name="first">The first channel.</param>
    /// <param name="second">The second channel.</param>
    /// <returns>The channel reader.</returns>
    public static ChannelReader<T> Create<T>(ChannelReader<T> first, ChannelReader<T> second)
        => new TwoSources<T>(first, second);

    class TwoSources<T> : ChannelReader<T>
    {
        readonly ChannelReader<T> first;
        readonly ChannelReader<T> second;

        public TwoSources(ChannelReader<T> first, ChannelReader<T> second)
        {
            this.first = first;
            this.second = second;
            this.Completion = Task.WhenAll(first.Completion, second.Completion);
        }

        public override Task Completion { get; }

        public override bool TryRead([MaybeNullWhen(false)] out T item)
        {
            return this.first.TryRead(out item) || this.second.TryRead(out item);
        }

        public override async ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
        {
            // TODO: Fix this class.
            Task<bool> t = await Task.WhenAny(
                this.first?.WaitToReadAsync(cancellationToken).AsTask(),
                this.second?.WaitToReadAsync(cancellationToken).AsTask());
            return await t;
        }
    }
}
