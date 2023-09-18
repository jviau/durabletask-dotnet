// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure;
using FluentAssertions;
using Xunit;

namespace Microsoft.DurableTask.Worker.AzureStorage.Tests;

public class StorageSerializerTests
{
    [Fact]
    public void OrchestrationMessage_BinaryData_RoundTrip()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int id = 1;
        OrchestrationMessage[] messages = new OrchestrationMessage[]
        {
            new GenericMessage(id++, now, "generic", "some data"),
            new OrchestratorStarted(now),
            new ExecutionStarted(now, "input value"),
            new ExecutionCompleted(id++, now, "result value", null),
            new ExecutionTerminated(-1, now, "reason"),
            //new ContinueAsNew(id++, now, "next input"),
            new EventSent(id++, now, Guid.NewGuid().ToString(), "some-event", null),
            new EventReceived(id++, now, "some-event", null),
            new TimerScheduled(id++, now, now.AddMilliseconds(100)),
            new TimerFired(id++, now, id - 2),
            new TaskActivityScheduled(id++, now, "activity", null),
            new TaskActivityCompleted(id++, now, id - 2 , null, null),
            new SubOrchestrationScheduled(id++, now, "orchestration", null),
            new SubOrchestrationCompleted(id++, now, id - 2, null, null),
        };

        foreach (OrchestrationMessage message in messages)
        {
            try
            {
                BinaryData data = StorageSerializer.Default.Serialize(
                    message, typeof(OrchestrationMessage));
                OrchestrationMessage result = data.ToObject<OrchestrationMessage>(StorageSerializer.Default)!;
                message.Should().Be(result);
            }
            catch
            {
                throw;
            }
        }
    }

    [Fact]
    public void OrchestrationMessage_String_RoundTrip()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int id = 1;
        OrchestrationMessage[] messages = new OrchestrationMessage[]
        {
            new GenericMessage(id++, now, "generic", "some data"),
            new OrchestratorStarted(now),
            new ExecutionStarted(now, "input value"),
            new ExecutionCompleted(id++, now, "result value", null),
            new ExecutionTerminated(-1, now, "reason"),
            //new ContinueAsNew(id++, now, "next input"),
            new EventSent(id++, now, Guid.NewGuid().ToString(), "some-event", null),
            new EventReceived(id++, now, "some-event", null),
            new TimerScheduled(id++, now, now.AddMilliseconds(100)),
            new TimerFired(id++, now, id - 2),
            new TaskActivityScheduled(id++, now, "activity", null),
            new TaskActivityCompleted(id++, now, id - 2 , null, null),
            new SubOrchestrationScheduled(id++, now, "orchestration", null),
            new SubOrchestrationCompleted(id++, now, id - 2, null, null),
        };

        foreach (OrchestrationMessage message in messages)
        {
            try
            {
                string json = JsonSerializer.Serialize(
                    message, typeof(OrchestrationMessage), StorageSerializer.Options);
                OrchestrationMessage result = JsonSerializer.Deserialize<OrchestrationMessage>(json, StorageSerializer.Options)!;
                message.Should().Be(result);
            }
            catch
            {
                throw;
            }
        }
    }
}
