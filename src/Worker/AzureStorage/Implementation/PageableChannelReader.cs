// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// Shims a <see cref="AsyncPageable{T}"/> to a <see cref="ChannelReader{T}"/>.
/// </summary>
/// <typeparam name="T">The type held by the async pageable.</typeparam>
class PageableChannelReader<T> : ChannelReader<T>, IAsyncDisposable
    where T : class
{
    readonly TaskCompletionSource<object?> completion = new();
    readonly IAsyncEnumerator<Page<T>> enumerator;
    int index;

    /// <summary>
    /// Initializes a new instance of the <see cref="PageableChannelReader{T}"/> class.
    /// </summary>
    /// <param name="pageable">The async pageable.</param>
    public PageableChannelReader(AsyncPageable<T> pageable)
    {
        this.enumerator = Check.NotNull(pageable).AsPages().GetAsyncEnumerator();
    }

    /// <inheritdoc/>
    public override Task Completion => this.completion.Task;

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => this.enumerator.DisposeAsync();

    /// <inheritdoc/>
    public override bool TryRead([MaybeNullWhen(false)] out T item)
    {
        if (this.CheckIndex())
        {
            item = this.enumerator.Current.Values[this.index++];
            return true;
        }

        item = null;
        return false;
    }

    /// <inheritdoc/>
    public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
    {
        async Task<bool> WaitToReadSlowAsync(ValueTask<bool> task)
        {
            await task;
            this.index = 0;
            return !this.CheckCompletion();
        }

        if (this.CheckIndex())
        {
            return new(true);
        }

        if (this.completion.Task.IsCompleted)
        {
            return new(false);
        }

        // We only allocate a task if we absolutely have to.
        ValueTask<bool> inner = this.enumerator.MoveNextAsync();
        if (inner.IsCompletedSuccessfully)
        {
            if (!inner.Result)
            {
                this.Complete();
            }

            this.CheckCompletion();
            this.index = 0;
            return inner;
        }

        return new(WaitToReadSlowAsync(inner));
    }

    void Complete()
    {
        this.completion.TrySetResult(true);
    }

    [MemberNotNullWhen(true, "enumerator")]
    bool CheckIndex()
    {
        return this.index < this.enumerator?.Current?.Values?.Count;
    }

    bool CheckCompletion()
    {
        if (!this.CheckIndex())
        {
            this.Complete();
            return true;
        }

        return false;
    }
}
