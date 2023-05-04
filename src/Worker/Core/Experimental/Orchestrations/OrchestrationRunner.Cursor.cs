// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using System.Xml;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Runs orchestrations.
/// </summary>
partial class OrchestrationRunner
{
    class Cursor
    {
        readonly Dictionary<int, PendingAction> actions = new();
        readonly OrchestrationWorkItem workItem;
        readonly ITaskOrchestrator orchestrator;

        Task<object?>? executionTask;

        int sequenceId;

        public Cursor(OrchestrationWorkItem workItem, DataConverter converter, ITaskOrchestrator orchestrator)
        {
            this.workItem = workItem;
            this.Converter = converter;
            this.orchestrator = orchestrator;
        }

        public DataConverter Converter { get; }

        protected ChannelReader<OrchestrationMessage> Reader => this.workItem.Channel.Reader;

        protected ChannelWriter<OrchestrationMessage> Writer => this.workItem.Channel.Writer;

        public int GetNextId() => this.sequenceId++;

        public async Task RunAsync()
        {
            while (await this.Reader.WaitToReadAsync())
            {
                while (this.Reader.TryRead(out OrchestrationMessage? message))
                {
                    this.HandleMessage(message);
                }

                if (await this.CheckForCompletionAsync())
                {
                    break;
                }
            }
        }

        public ValueTask SendMessage(OrchestrationMessage message)
        {
            return this.SendMessageCoreAsync(message).AsValueTask();
        }

        public async Task<T> SendMessageAsync<T>(OrchestrationMessage message)
        {
            PendingAction action = await this.SendMessageCoreAsync(message);
            return await action.GetResultAsync<T>(this.Converter);
        }

        async ValueTask<PendingAction> SendMessageCoreAsync(OrchestrationMessage message)
        {
            Check.NotNull(message);
            if (message is not IOrchestrationAction)
            {
                throw new InvalidOperationException("Message is not a valid outbound orchestration action.");
            }

            if (this.actions.ContainsKey(message.Id))
            {
                throw new InvalidOperationException("Duplicate action ID");
            }

            PendingAction action = new(message);
            this.actions[message.Id] = action;

            if (!this.workItem.IsReplaying)
            {
                // TODO: cancellation?.
                await this.Writer.WriteAsync(message);
            }

            return action;
        }

        void HandleMessage(OrchestrationMessage message)
        {
            switch (message)
            {
                case ExecutionStarted m: this.ExecutionStarted(m); break;
                case WorkScheduledMessage m: this.WorkScheduled(m); break;
                case WorkCompletedMessage m: this.WorkCompleted(m); break;
                default: throw new InvalidOperationException($"Unknown or invalid message {message?.GetType()}");
            }
        }

        void ExecutionStarted(ExecutionStarted message)
        {
            TaskOrchestrationContext context = new Context(this.workItem, this);
            object? input = this.Converter.Deserialize(message.Input, this.orchestrator.InputType);
            this.executionTask = this.orchestrator.RunAsync(context, input);
        }

        void WorkScheduled(WorkScheduledMessage message)
        {
            if (!this.actions.TryGetValue(message.Id, out PendingAction action))
            {
                throw new InvalidOperationException("Non deterministic");
            }

            action.Validate(message);
        }

        void WorkCompleted(WorkCompletedMessage message)
        {
            if (!this.actions.TryGetValue(message.ScheduledId, out PendingAction action))
            {
                // duplicate event?
                return;
            }

            if (message.Failure is { } failure)
            {
                action.Fail(failure);
            }
            else
            {
                action.Succeed(message.Result);
            }

            this.actions.Remove(message.ScheduledId);
        }

        async ValueTask<bool> CheckForCompletionAsync()
        {
            if (!(this.executionTask?.IsCompleted ?? false))
            {
                return false;
            }

            ExecutionCompleted? completed;
            if (this.executionTask.TryGetException(out Exception? ex))
            {
                if (ex is AbortWorkItemException)
                {
                    this.Writer.Complete(ex);
                    return true;
                }

                completed = new ExecutionCompleted(
                    this.GetNextId(),
                    null,
                    TaskFailureDetails.FromException(ex));
            }
            else
            {
                object? result = this.executionTask.GetResultAssumesCompleted();
                completed = new ExecutionCompleted(
                    this.GetNextId(),
                    this.Converter.Serialize(result),
                    null);
            }

            await this.Writer.WriteAsync(completed);
            this.Writer.Complete();
            return true;
        }
    }
}
