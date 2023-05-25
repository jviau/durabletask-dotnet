﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static Microsoft.DurableTask.Protobuf.Experimental.DurableTaskClient;
using P = Microsoft.DurableTask.Protobuf.Experimental;

namespace Microsoft.DurableTask.Client.Grpc.Stream;

/// <summary>
/// Stream based gRPC client.
/// </summary>
public sealed class GrpcDurableTaskClient : DurableTaskClient
{
    readonly ILogger logger;
    readonly DurableTaskClientClient client;
    readonly GrpcDurableTaskClientOptions options;
    AsyncDisposable asyncDisposable;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcDurableTaskClient"/> class.
    /// </summary>
    /// <param name="name">The name of the client.</param>
    /// <param name="options">The gRPC client options.</param>
    /// <param name="logger">The logger.</param>
    public GrpcDurableTaskClient(
        string name, IOptionsMonitor<GrpcDurableTaskClientOptions> options, ILogger<GrpcDurableTaskClient> logger)
        : this(name, Check.NotNull(options).Get(name), logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcDurableTaskClient"/> class.
    /// </summary>
    /// <param name="name">The name of the client.</param>
    /// <param name="options">The gRPC client options.</param>
    /// <param name="logger">The logger.</param>
    public GrpcDurableTaskClient(string name, GrpcDurableTaskClientOptions options, ILogger logger)
        : base(name)
    {
        this.logger = Check.NotNull(logger);
        this.options = Check.NotNull(options);
        this.asyncDisposable = BuildChannel(options, out GrpcChannel channel);
        this.client = new DurableTaskClientClient(channel);
    }

    DataConverter DataConverter => this.options.DataConverter;

    /// <inheritdoc/>
    public override ValueTask DisposeAsync()
    {
        return this.asyncDisposable.DisposeAsync();
    }

    /// <inheritdoc/>
    public override AsyncPageable<OrchestrationMetadata> GetAllInstancesAsync(OrchestrationQuery? filter = null)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public override async Task<OrchestrationMetadata?> GetInstancesAsync(
        string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
    {
        Check.NotNullOrEmpty(instanceId);
        P.GetOrchestrationRequest request = CreateGetRequest(instanceId, getInputsAndOutputs);

        try
        {
            P.OrchestrationInfoResponse response = await this.client.GetOrchestrationAsync(
                request, cancellationToken: cancellation);
            return this.CreateMetadata(response, getInputsAndOutputs);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException(null, ex, cancellation);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public override Task<PurgeResult> PurgeAllInstancesAsync(
        PurgeInstancesFilter filter, CancellationToken cancellation = default)
    {
        Check.NotNull(filter);
        P.PurgeOrchestrationsRequest request = new()
        {
            Filter = new()
            {
                CreatedFrom = filter.CreatedFrom?.ToTimestamp(),
                CreatedTo = filter.CreatedTo?.ToTimestamp(),
            },
        };

        if (filter.Statuses is { } statuses)
        {
            request.Filter.IncludeStates.AddRange(statuses.Select(ToGrpc));
        }

        return this.PurgeCoreAsync(request, cancellation);
    }

    /// <inheritdoc/>
    public override Task<PurgeResult> PurgeInstanceAsync(string instanceId, CancellationToken cancellation = default)
    {
        Check.NotNullOrEmpty(instanceId);
        return this.PurgeCoreAsync(new P.PurgeOrchestrationsRequest { InstanceId = instanceId }, cancellation);
    }

    /// <inheritdoc/>
    public override async Task RaiseEventAsync(
        string instanceId, string eventName, object? eventPayload = null, CancellationToken cancellation = default)
    {
        Check.NotNullOrEmpty(instanceId);
        Check.NotNullOrEmpty(eventName);

        P.RaiseEventRequest request = new()
        {
            InstanceId = instanceId,
            Name = eventName,
            Input = this.DataConverter.Serialize(eventPayload),
        };

        try
        {
            await this.client.RaiseOrchestrationEventAsync(request, cancellationToken: cancellation);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException(null, ex, cancellation);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            // what do?
            throw;
        }
    }

    /// <inheritdoc/>
    public override Task ResumeInstanceAsync(
        string instanceId, string? reason = null, CancellationToken cancellation = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public override async Task<string> ScheduleNewOrchestrationInstanceAsync(
        TaskName orchestratorName,
        object? input = null,
        StartOrchestrationOptions? options = null,
        CancellationToken cancellation = default)
    {
        Check.NotDefault(orchestratorName);
        P.ScheduleOrchestrationRequest request = new()
        {
            Name = orchestratorName.ToGrpcName(),
            Input = this.DataConverter.Serialize(input),
        };

        if (options is not null)
        {
            request.Options = new()
            {
                InstanceId = options.InstanceId,
                StartAt = options.StartAt?.ToTimestamp(),
            };
        }

        P.OrchestrationInfoResponse response = await this.client.ScheduleOrchestrationAsync(
            request, cancellationToken: cancellation);
        return response.Id.InstanceId;
    }

    /// <inheritdoc/>
    public override Task SuspendInstanceAsync(
        string instanceId, string? reason = null, CancellationToken cancellation = default)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public override async Task TerminateInstanceAsync(
        string instanceId, object? output = null, CancellationToken cancellation = default)
    {
        Check.NotNullOrEmpty(instanceId);
        P.TerminateOrchestrationRequest request = new()
        {
            InstanceId = instanceId,
            Reason = this.DataConverter.Serialize(output),
        };

        try
        {
            await this.client.TerminateOrchestrationAsync(request, cancellationToken: cancellation);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException(null, ex, cancellation);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            // what do?
            throw;
        }
    }

    /// <inheritdoc/>
    public override async Task<OrchestrationMetadata> WaitForInstanceCompletionAsync(
        string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
    {
        Check.NotNullOrEmpty(instanceId);
        while (true)
        {
            OrchestrationMetadata? metadata = await this.GetInstanceAsync(
                instanceId, getInputsAndOutputs, cancellation)
                ?? throw new InvalidOperationException("Not found");

            if (metadata.IsCompleted)
            {
                return metadata;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellation);
        }
    }

    /// <inheritdoc/>
    public override async Task<OrchestrationMetadata> WaitForInstanceStartAsync(
        string instanceId, bool getInputsAndOutputs = false, CancellationToken cancellation = default)
    {
        Check.NotNullOrEmpty(instanceId);
        while (true)
        {
            OrchestrationMetadata? metadata = await this.GetInstanceAsync(
                instanceId, getInputsAndOutputs, cancellation)
                ?? throw new InvalidOperationException("Not found");

            if (metadata.RuntimeStatus != OrchestrationRuntimeStatus.Pending)
            {
                return metadata;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellation);
        }
    }

    static AsyncDisposable BuildChannel(GrpcDurableTaskClientOptions options, out GrpcChannel channel)
    {
        if (options.Channel is GrpcChannel c)
        {
            channel = c;
            return default;
        }

        c = GetChannel(options.Address);
        channel = c;
        return new AsyncDisposable(async () => await c.ShutdownAsync());
    }

#if NET6_0_OR_GREATER
    static GrpcChannel GetChannel(string? address)
    {
        if (string.IsNullOrEmpty(address))
        {
            address = "http://localhost:4001";
        }

        return GrpcChannel.ForAddress(address);
    }
#endif

#if NETSTANDARD2_0
    static GrpcChannel GetChannel(string? address)
    {
        if (string.IsNullOrEmpty(address))
        {
            address = "localhost:4001";
        }

        return new(address, ChannelCredentials.Insecure);
    }
#endif

    static P.GetOrchestrationRequest CreateGetRequest(string instanceId, bool includeInputsAndOutputs)
    {
        P.GetOrchestrationRequest request = new() { InstanceId = instanceId };
        if (includeInputsAndOutputs)
        {
            request.Expand.Add(P.ExpandOrchestrationDetail.Input);
            request.Expand.Add(P.ExpandOrchestrationDetail.Output);
        }

        return request;
    }

    static bool IsTerminal(P.OrchestrationInfoResponse response)
    {
        return response.Status.Status
            is P.OrchestrationState.Canceled or P.OrchestrationState.Completed or P.OrchestrationState.Failed
            or P.OrchestrationState.Terminated;
    }

    static bool HasStarted(P.OrchestrationInfoResponse response)
    {
        return response.Status.Status is not P.OrchestrationState.Pending;
    }

    static P.OrchestrationState ToGrpc(OrchestrationRuntimeStatus status)
    {
#pragma warning disable 0618 // Referencing Obsolete member. This is intention as we are only converting it.
        return status switch
        {
            OrchestrationRuntimeStatus.Canceled => P.OrchestrationState.Canceled,
            OrchestrationRuntimeStatus.Completed => P.OrchestrationState.Completed,
            OrchestrationRuntimeStatus.Failed => P.OrchestrationState.Failed,
            OrchestrationRuntimeStatus.Pending => P.OrchestrationState.Pending,
            OrchestrationRuntimeStatus.Running => P.OrchestrationState.Running,
            OrchestrationRuntimeStatus.Terminated => P.OrchestrationState.Terminated,
            OrchestrationRuntimeStatus.Suspended => P.OrchestrationState.Suspended,
            _ => P.OrchestrationState.Unspecified,
        };
#pragma warning restore 0618 // Referencing Obsolete member.
    }

    async Task<PurgeResult> PurgeCoreAsync(P.PurgeOrchestrationsRequest request, CancellationToken cancellation)
    {
        try
        {
            P.PurgeOrchestrationsResponse response = await this.client.PurgeOrchestrationsAsync(
                request, cancellationToken: cancellation);
            return new PurgeResult(response.PurgedCount);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException(null, ex, cancellation);
        }
    }

    OrchestrationMetadata CreateMetadata(P.OrchestrationInfoResponse state, bool includeInputsAndOutputs)
    {
        return new(state.Name.Name, state.Id.InstanceId)
        {
            CreatedAt = state.CreatedAt.ToDateTimeOffset(),
            LastUpdatedAt = state.LastUpdatedAt.ToDateTimeOffset(),
            RuntimeStatus = state.Status.Status.Convert(),
            SerializedInput = state.Input,
            SerializedOutput = state.Output,
            SerializedCustomStatus = state.Status.SubStatus,
            FailureDetails = state.Error?.Convert(),
            DataConverter = includeInputsAndOutputs ? this.DataConverter : null,
        };
    }
}
