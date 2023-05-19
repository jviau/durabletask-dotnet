// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Protobuf.Experimental;

namespace Microsoft.DurableTask.Grpc.Hub;

/// <summary>
/// Flags enum for what values of an orchestration state to expand.
/// </summary>
[Flags]
enum OrchestrationExpandDetail
{
    /// <summary>
    /// Expand nothing.
    /// </summary>
    None = 0,

    /// <summary>
    /// Expand input.
    /// </summary>
    Input = 1,

    /// <summary>
    /// Expand output.
    /// </summary>
    Output = 1 << 1,

    /// <summary>
    /// Expand metadata.
    /// </summary>
    Metadata = 1 << 2,

    /// <summary>
    /// Expand all.
    /// </summary>
    All = Input | Output | Metadata,
}

/// <summary>
/// Extensions for <see cref="OrchestrationExpandDetail"/>.
/// </summary>
static class OrchestrationExpandDetailExtensions
{
    /// <summary>
    /// Convert a <see cref="IEnumerable{ExpandOrchestrationDetail}"/> to a single <see cref="OrchestrationExpandDetail"/>.
    /// </summary>
    /// <param name="proto">The enum to convert.</param>
    /// <returns>The converted enum.</returns>
    public static OrchestrationExpandDetail FromProto(IEnumerable<ExpandOrchestrationDetail> proto)
    {
        OrchestrationExpandDetail result = OrchestrationExpandDetail.None;
        foreach (ExpandOrchestrationDetail detail in proto)
        {
            result |= FromProto(detail);
        }

        return result;
    }

    /// <summary>
    /// Convert a <see cref="ExpandOrchestrationDetail"/> to a <see cref="OrchestrationExpandDetail"/>.
    /// </summary>
    /// <param name="proto">The enum to convert.</param>
    /// <returns>The converted enum.</returns>
    public static OrchestrationExpandDetail FromProto(ExpandOrchestrationDetail proto)
    {
        return proto switch
        {
            ExpandOrchestrationDetail.Input => OrchestrationExpandDetail.Input,
            ExpandOrchestrationDetail.Output => OrchestrationExpandDetail.Output,
            ExpandOrchestrationDetail.Metadata => OrchestrationExpandDetail.Metadata,
            _ => OrchestrationExpandDetail.None,
        };
    }
}
