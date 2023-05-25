// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Runs orchestrations.
/// </summary>
partial class OrchestrationRunner
{
    class Cursor
    {
        readonly Dictionary<int, PendingAction> actions = new();
        readonly ITaskOrchestrator orchestrator;
        readonly ExternalEventSource externalEvent;
        readonly ILogger logger;
        readonly CancellationToken cancellation;

        ExecutionCompleted? pendingCompletion;
        bool preserveUnprocessedEvents;
        Task<object?>? executionTask;

        int sequenceId;

        public Cursor(
            OrchestrationWorkItem workItem,
            OrchestrationRunnerOptions options,
            ITaskOrchestrator orchestrator,
            ILoggerFactory loggerFactory,
            CancellationToken cancellation)
        {
            this.WorkItem = workItem;
            this.Options = options;
            this.orchestrator = orchestrator;
            this.LoggerFactory = loggerFactory;
            this.logger = loggerFactory.CreateLogger<OrchestrationRunner>();
            this.cancellation = cancellation;
            this.externalEvent = new(options.DataConverter);
        }

        public DateTimeOffset CurrentDateTime { get; private set; }

        public OrchestrationWorkItem WorkItem { get; }

        public OrchestrationRunnerOptions Options { get; }

        public DataConverter Converter => this.Options.DataConverter;

        public ILoggerFactory LoggerFactory { get; }

        public object? Input { get; private set; }

        ChannelReader<OrchestrationMessage> Reader => this.WorkItem.Channel.Reader;

        ChannelWriter<OrchestrationMessage> Writer => this.WorkItem.Channel.Writer;

        public int GetNextId() => this.sequenceId++;

        public async Task RunAsync()
        {
            try
            {
                while (await this.Reader.WaitToReadAsync(this.cancellation))
                {
                    while (this.Reader.TryRead(out OrchestrationMessage? message))
                    {
                        if (!isOrchestratorThread)
                        {

                        }

                        this.logger.LogTrace("Received message of type {MessageType}", message.GetType());
                        this.HandleMessage(message);
                    }

                    if (await this.CheckForCompletionAsync())
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                this.Writer.TryComplete(ex);
                throw;
            }

            this.Writer.TryComplete();
        }

        public void SetCustomStatus(object? status)
        {
            if (!this.WorkItem.IsReplaying)
            {
                this.WorkItem.CustomStatus = this.Converter.Serialize(status);
            }
        }

        public void ContinueAsNew(object? input, bool preserveUnprocessedEvents)
        {
            if (this.pendingCompletion is not null)
            {
                return;
            }

            ContinueAsNew completion = new(
                this.GetNextId(),
                DateTimeOffset.UtcNow,
                this.Converter.Serialize(input));
            this.pendingCompletion = completion;
            this.preserveUnprocessedEvents = preserveUnprocessedEvents;

            if (preserveUnprocessedEvents)
            {
                foreach (EventReceived message in this.externalEvent.DrainBuffer())
                {
                    completion.CarryOverMessages.Add(message);
                }
            }
        }

        public Task<T> WaitExternalEventAsync<T>(string name, CancellationToken cancellation = default)
        {
            return this.externalEvent.WaitAsync<T>(name, cancellation);
        }

        public async Task<T> RunActionAsync<T>(OrchestrationAction action)
        {
            PendingAction pending = await this.DispatchActionAsync(action);
            string? result = await pending.WaitAsync();
            return this.Converter.Deserialize<T>(result)!;
        }

        public async Task RunActionAsync(OrchestrationAction action)
        {
            PendingAction pending = await this.DispatchActionAsync(action);
            await pending.WaitAsync();
        }

        async ValueTask<PendingAction> DispatchActionAsync(OrchestrationAction action)
        {
            Check.NotNull(action);
            if (action.Id != (this.sequenceId - 1))
            {
                // TODO: this may be problematic. It means the TaskOrchestrationContext MUST call RunActionAsync for
                // every GetNextId call.
                throw new InvalidOperationException("Unexpected action ID");
            }

            PendingAction pending = new(action);
            this.actions[action.Id] = pending;

            if (!this.WorkItem.IsReplaying)
            {
                await this.Writer.WriteAsync(action.ToMessage(this.Converter), this.cancellation);
            }

            return pending;
        }

        void HandleMessage(OrchestrationMessage message)
        {
            if (message.Timestamp > this.CurrentDateTime)
            {
                // We track current time based on the progression of incoming message timestamps.
                this.CurrentDateTime = message.Timestamp;
            }

            switch (message)
            {
                case ExecutionStarted m: this.OnExecutionStarted(m); break;
                case ExecutionTerminated m: this.OnExecutionTerminated(m); break;
                case WorkScheduledMessage m: this.OnOutboundWork(m); break;
                case WorkCompletedMessage m: this.OnWorkCompleted(m); break;
                case EventReceived m: this.OnEventReceived(m); break;
                case EventSent m: this.OnOutboundWork(m); break;
                case TimerScheduled m: this.OnOutboundWork(m); break;
                case TimerFired m: this.OnTimerFired(m); break;
                case OrchestratorStarted: break; // no-op
                default:
                    this.logger.LogTrace(
                        "Orchestration message of type {MessageType} was unhandled", message?.GetType());
                    break;
            }
        }

        void OnExecutionStarted(ExecutionStarted message)
        {
            TaskOrchestrationContext context = new Context(this);
            this.Input = this.Converter.Deserialize(message.Input, this.orchestrator.InputType);
            this.executionTask = this.orchestrator.RunAsync(context, this.Input);
        }

        void OnExecutionTerminated(ExecutionTerminated message)
        {
            // Termination takes precedence over any ContinueAsNew calls.
            this.pendingCompletion = message with { Id = this.GetNextId() };
            this.preserveUnprocessedEvents = false;
        }

        void OnOutboundWork(OrchestrationMessage message)
        {
            if (!this.actions.TryGetValue(message.Id, out PendingAction action))
            {
                throw new InvalidOperationException("Non deterministic");
            }

            action.Consume(message);
            if (action.FireAndForget)
            {
                this.actions.Remove(message.Id);
            }
        }

        void OnTimerFired(TimerFired message)
        {
            if (!this.actions.TryGetValue(message.ScheduledId, out PendingAction action))
            {
                // duplicate?
                return;
            }

            action.Succeed(null);
            this.actions.Remove(message.ScheduledId);
        }

        void OnWorkCompleted(WorkCompletedMessage message)
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

        void OnEventReceived(EventReceived message)
        {
            if (this.preserveUnprocessedEvents && this.pendingCompletion is ContinueAsNew continueAsNew)
            {
                continueAsNew.CarryOverMessages.Add(message);
                return;
            }

            this.externalEvent.OnExternalEvent(message);
        }

        async ValueTask<bool> CheckForCompletionAsync()
        {
            // Completion priority:
            // 1. ContinueAsNew
            // 2. Completed executionTask
            // 3. All others (eg. termination)
            if (this.pendingCompletion is ContinueAsNew continueAsNew)
            {
                // 1. ContinueAsNew - highest priority.
                await this.Writer.WriteAsync(continueAsNew, this.cancellation);
                this.Writer.TryComplete();
                return true;
            }

            if (this.executionTask?.IsCompleted ?? false)
            {
                // 2. Completed executionTask
                ExecutionCompleted? completed;
                if (this.executionTask.TryGetException(out Exception? ex))
                {
                    if (ex is AbortWorkItemException)
                    {
                        this.Writer.TryComplete(ex);
                        return true;
                    }

                    completed = new ExecutionCompleted(
                        this.GetNextId(),
                        DateTimeOffset.UtcNow,
                        null,
                        TaskFailureDetails.FromException(ex, includeStackTrace: true));
                }
                else
                {
                    object? result = this.executionTask.GetResultAssumesCompleted();
                    completed = new ExecutionCompleted(
                        this.GetNextId(),
                        DateTimeOffset.UtcNow,
                        this.Converter.Serialize(result),
                        null);
                }

                await this.Writer.WriteAsync(completed, this.cancellation);
                this.Writer.TryComplete();
                return true;
            }

            if (this.pendingCompletion is { } pending)
            {
                // 3. All others.
                await this.Writer.WriteAsync(pending, this.cancellation);
                this.Writer.TryComplete();
                return true;
            }

            return false;
        }
    }
}
