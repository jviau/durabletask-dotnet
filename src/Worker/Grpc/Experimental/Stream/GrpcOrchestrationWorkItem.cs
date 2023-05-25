// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using static Microsoft.DurableTask.Protobuf.Experimental.DurableTaskHub;
using P = Microsoft.DurableTask.Protobuf.Experimental;

namespace Microsoft.DurableTask.Worker.Grpc.Stream;

/// <summary>
/// An activity work item from the gRPC sidecar.
/// </summary>
class GrpcOrchestrationWorkItem : OrchestrationWorkItem
{
    readonly DurableTaskHubClient client;
    GrpcOrchestrationChannel? channel;
    string? status;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcOrchestrationWorkItem"/> class.
    /// </summary>
    /// <param name="request">The gRPC orchestration request.</param>
    /// <param name="client">The task hub client.</param>
    public GrpcOrchestrationWorkItem(P.OrchestratorWorkItem request, DurableTaskHubClient client)
        : base(Check.NotNull(request).Id.InstanceId, request.Name.Convert())
    {
        this.client = Check.NotNull(client);
        this.status = request.SubStatus;

        if (request.Parent is { } p)
        {
            this.Parent = new(request.Parent.Name.Convert(), request.Parent.Id.InstanceId);
        }
    }

    /// <inheritdoc/>
    public override ParentOrchestrationInstance? Parent { get; }

    /// <inheritdoc/>
    public override string? CustomStatus
    {
        get => this.status;
        set
        {
            this.VerifyChannel().SetStatus(value);
            this.status = value;
        }
    }

    /// <inheritdoc/>
    public override bool IsReplaying => this.VerifyChannel().IsReplaying;

    /// <inheritdoc/>
    public override Channel<OrchestrationMessage> Channel => this.channel ??= new(this.Id, this.client);

    /// <inheritdoc/>
    public override Task ReleaseAsync(CancellationToken cancellation = default)
    {
        if (this.channel is { } c)
        {
            this.channel = null;
            return c.DisposeAsync().AsTask();
        }

        return Task.CompletedTask;
    }

    GrpcOrchestrationChannel VerifyChannel()
    {
        if (this.channel is { } c)
        {
            return c;
        }

        throw new InvalidOperationException("Channel has not yet been initialized.");
    }
}
