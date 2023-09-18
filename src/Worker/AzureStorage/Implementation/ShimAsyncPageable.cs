// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// Shims <see cref="Azure.AsyncPageable{T}"/> to <see cref="AsyncPageable{T}"/>.
/// </summary>
/// <typeparam name="T">The type held by the pages.</typeparam>
class ShimAsyncPageable<T> : AsyncPageable<T>
    where T : class
{
    readonly Azure.AsyncPageable<T> inner;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShimAsyncPageable{T}"/> class.
    /// </summary>
    /// <param name="inner">The inner pageable.</param>
    public ShimAsyncPageable(Azure.AsyncPageable<T> inner)
    {
        this.inner = Check.NotNull(inner);
    }

    /// <inheritdoc/>
    public async override IAsyncEnumerable<Page<T>> AsPages(string? continuationToken = null, int? pageSizeHint = null)
    {
        await foreach (Azure.Page<T> page in this.inner.AsPages(continuationToken, pageSizeHint))
        {
            yield return new Page<T>(page.Values, page.ContinuationToken);
        }
    }
}

/// <summary>
/// Shims <see cref="Azure.AsyncPageable{T}"/> to <see cref="AsyncPageable{T}"/>.
/// </summary>
/// <typeparam name="TIn">The incoming type.</typeparam>
/// <typeparam name="TOut">The type held by the pages.</typeparam>
class ShimAsyncPageable<TIn, TOut> : AsyncPageable<TOut>
    where TIn : class
    where TOut : class
{
    readonly Azure.AsyncPageable<TIn> inner;
    readonly Func<TIn, TOut> select;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShimAsyncPageable{TIn, TOut}"/> class.
    /// </summary>
    /// <param name="inner">The inner pageable.</param>
    /// <param name="select">The select statement to transform the values.</param>
    public ShimAsyncPageable(Azure.AsyncPageable<TIn> inner, Func<TIn, TOut> select)
    {
        this.inner = Check.NotNull(inner);
        this.select = Check.NotNull(select);
    }

    /// <inheritdoc/>
    public async override IAsyncEnumerable<Page<TOut>> AsPages(
        string? continuationToken = null, int? pageSizeHint = null)
    {
        await foreach (Azure.Page<TIn> page in this.inner.AsPages(continuationToken, pageSizeHint))
        {
            yield return new Page<TOut>(page.Values.Select(this.select).ToList(), page.ContinuationToken);
        }
    }
}
