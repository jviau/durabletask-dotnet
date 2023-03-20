// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;

namespace Microsoft.DurableTask.Converters;

/// <summary>
/// An implementation of <see cref="DataConverter"/> that uses System.Text.Json APIs for data serialization.
/// </summary>
public class JsonDataConverter : DataConverter
{
    // WARNING: Changing default serialization options could potentially be breaking for in-flight orchestrations.
    static readonly JsonSerializerOptions DefaultOptions = new()
    {
        IncludeFields = true,
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonDataConverter"/> class.
    /// </summary>
    /// <param name="options">The serializer options.</param>
    public JsonDataConverter(JsonSerializerOptions? options = null)
    {
        this.Options = options ?? DefaultOptions;
    }

    /// <summary>
    /// Gets an instance of the <see cref="JsonDataConverter"/> with default configuration.
    /// </summary>
    public static JsonDataConverter Default { get; } = new JsonDataConverter();

    /// <summary>
    /// Gets the JSON serializer options.
    /// </summary>
    public JsonSerializerOptions Options { get; }

    /// <inheritdoc/>
    public override string? Serialize(object? value)
    {
        return value != null ? JsonSerializer.Serialize(value, this.Options) : null;
    }

    /// <inheritdoc/>
    public override object? Deserialize(string? data, Type targetType)
    {
        return data != null ? JsonSerializer.Deserialize(data, targetType, this.Options) : null;
    }
}
