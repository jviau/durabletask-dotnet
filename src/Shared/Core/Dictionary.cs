// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.ObjectModel;

namespace Microsoft.DurableTask;

/// <summary>
/// Dictionary helpers.
/// </summary>
static class Dictionary
{
    /// <summary>
    /// Read-only dictionary helpers.
    /// </summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The value type.</typeparam>
    public static class ReadOnly<TKey, TValue>
            where TKey : notnull
    {
        /// <summary>
        /// Gets the empty readonly dictionary.
        /// </summary>
        public static readonly IReadOnlyDictionary<TKey, TValue> Empty
            = new ReadOnlyDictionary<TKey, TValue>(new Dictionary<TKey, TValue>());
    }
}
