﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.


namespace Microsoft.DurableTask.Benchmarks.EndToEnd;

public static class ScaleArguments
{
    public static IEnumerable<object[]> Values
    {
        get
        {
            //yield return new object[] { 10, 10 };
            //yield return new object[] { 100, 1 };
            yield return new object[] { 100, 100 };
        }
    }
}
