// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;

namespace Microsoft.DurableTask.Converters.Tests;

public class JsonDataConverterTests
{
    [Fact]
    public void ConvertStream_Success()
    {
        JsonDataConverter converter = new();

        var obj = new { PropA = "A", PropB = "B" };
        using var stream = new MemoryStream();
        JsonSerializer.Serialize(stream, obj);

        stream.Position = 0;
        string? results = converter.Serialize(stream);
    }
}
