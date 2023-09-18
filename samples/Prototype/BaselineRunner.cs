// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Prototype;

static partial class BaselineRunner
{
    public static Runner Create(BaselineOptions options)
    {
        return options.Mode switch
        {
            0 => new CoreRunner(options),
            1 => new ChannelRunner(options),
            2 => new StreamingRunner(options),
            _ => throw new ArgumentException(nameof(options.Mode)),
        };
    }
}
