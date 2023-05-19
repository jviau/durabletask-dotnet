// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using static Microsoft.DurableTask.Protobuf.TaskHubSidecarService;
using P = Microsoft.DurableTask.Protobuf;

namespace Microsoft.DurableTask.Worker.Grpc.Bulk;

/// <summary>
/// An activity work item from the gRPC sidecar.
/// </summary>
class GrpcOrchestrationWorkItem : OrchestrationWorkItem
{
    readonly GrpcOrchestrationChannel channel;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcOrchestrationWorkItem"/> class.
    /// </summary>
    /// <param name="request">The gRPC orchestration request.</param>
    /// <param name="sidecar">The sidecar service.</param>
    public GrpcOrchestrationWorkItem(P.OrchestratorRequest request, TaskHubSidecarServiceClient sidecar)
        : base(request.InstanceId, GetName(request))
    {
        this.channel = new GrpcOrchestrationChannel(request, sidecar);
    }

    /// <inheritdoc/>
    public override ParentOrchestrationInstance? Parent { get; } // TODO: set this

    /// <inheritdoc/>
    public override string? CustomStatus
    {
        get => this.channel.CustomStatus;
        set => this.channel.CustomStatus = value;
    }

    /// <inheritdoc/>
    public override bool IsReplaying => this.channel.IsReplaying;

    /// <inheritdoc/>
    public override Channel<OrchestrationMessage> Channel => this.channel;

    /// <inheritdoc/>
    public override Task ReleaseAsync() => this.channel.FlushAsync();

    static TaskName GetName(P.OrchestratorRequest request)
    {
        foreach (P.HistoryEvent? e in request.PastEvents)
        {
            if (e?.EventTypeCase == P.HistoryEvent.EventTypeOneofCase.ExecutionStarted)
            {
                return e.ExecutionStarted.Name;
            }
        }

        foreach (P.HistoryEvent? e in request.NewEvents)
        {
            if (e?.EventTypeCase == P.HistoryEvent.EventTypeOneofCase.ExecutionStarted)
            {
                return e.ExecutionStarted.Name;
            }
        }

        throw new InvalidOperationException("The provided orchestration history was incomplete");
    }
}
