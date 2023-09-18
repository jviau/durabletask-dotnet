// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// Struct representing an optionally supplied value.
/// </summary>
/// <remarks>
/// This is used to differentiate between:
/// 1. No value provided
/// 2. Default value provided
/// 3. Value provided.
/// </remarks>
/// <typeparam name="T">The value held.</typeparam>
readonly struct Optional<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Optional{T}"/> struct.
    /// </summary>
    /// <param name="value">The value.</param>
    public Optional(T? value)
    {
        this.Value = value;
        this.HasValue = true;
    }

    /// <summary>
    /// Gets the provided value.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Gets a value indicating whether this optional has a value supplied to it.
    /// </summary>
    public bool HasValue { get; }

    public static implicit operator T?(Optional<T> value) => value.Value;

    public static implicit operator Optional<T>(T? value) => new(value);
}
