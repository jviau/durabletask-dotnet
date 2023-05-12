// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;

namespace Microsoft.DurableTask;

/// <summary>
/// Extensions for primitive types.
/// </summary>
static class PrimitiveExtensions
{
    /// <summary>
    /// Converts an <see cref="int" /> to <see cref="string" /> with the <see cref="CultureInfo.InvariantCulture" />.
    /// </summary>
    /// <param name="i">The int to convert.</param>
    /// <returns>The int as a string.</returns>
    public static string ToStringInvariant(this int i)
        => i.ToString(CultureInfo.InvariantCulture);
}
