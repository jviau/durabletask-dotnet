// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Azure.Core.Serialization;

namespace Microsoft.DurableTask.Worker.AzureStorage;

/// <summary>
/// Contains default serializer.
/// </summary>
static class StorageSerializer
{
    /// <summary>
    /// Gets the JSON serializer options.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new PolymorphicTypeResolver(),
    };

    /// <summary>
    /// Gets the default object serializer.
    /// </summary>
    public static readonly ObjectSerializer Default = new JsonObjectSerializer(Options);

    /// <summary>
    /// Convert the provided value to it's binary representation and return it as a <see cref="BinaryData"/> instance.
    /// </summary>
    /// <param name="serializer">The object serializer.</param>
    /// <param name="value">The value to convert.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to use during serialization.</param>
    /// <returns>The object's binary representation as <see cref="BinaryData"/>.</returns>
    public static ValueTask<BinaryData> SerializeAsync(
        this ObjectSerializer serializer, object? value, CancellationToken cancellationToken)
    {
        Check.NotNull(serializer);
        return serializer.SerializeAsync(value, null, cancellationToken);
    }

    class PolymorphicTypeResolver : DefaultJsonTypeInfoResolver
    {
        public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            JsonTypeInfo jsonTypeInfo = base.GetTypeInfo(type, options);

            if (typeof(OrchestrationMessage) == type)
            {
                jsonTypeInfo.PolymorphismOptions = new()
                {
                    IgnoreUnrecognizedTypeDiscriminators = false,
                    UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
                };

                foreach (JsonDerivedType derived in GetDerivedTypes())
                {
                    jsonTypeInfo.PolymorphismOptions.DerivedTypes.Add(derived);
                }
            }

            return jsonTypeInfo;
        }

        static IEnumerable<JsonDerivedType> GetDerivedTypes()
        {
            Type baseType = typeof(OrchestrationMessage);
            foreach (Type type in baseType.Assembly.GetTypes())
            {
                if (!type.IsAbstract && type.IsPublic && baseType.IsAssignableFrom(type))
                {
                    // TODO: short type discriminator value for performance.
                    yield return new(type, type.Name);
                }
            }
        }
    }
}
