// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reactive;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Runs orchestrations.
/// </summary>
partial class OrchestrationRunner
{
    class Context : TaskOrchestrationContext
    {
        readonly OrchestrationWorkItem workItem;
        readonly Cursor cursor;

        public Context(OrchestrationWorkItem workItem, Cursor cursor)
        {
            this.workItem = workItem;
            this.cursor = cursor;
        }

        /// <inheritdoc/>
        public override TaskName Name => this.workItem.Name;

        /// <inheritdoc/>
        public override string InstanceId => this.workItem.Id;

        /// <inheritdoc/>
        public override ParentOrchestrationInstance? Parent => this.workItem.Parent;

        public override DateTime CurrentUtcDateTime => throw new NotImplementedException();

        public override bool IsReplaying => this.workItem.IsReplaying;

        protected override ILoggerFactory LoggerFactory => throw new NotImplementedException();

        public override Task<TResult> CallActivityAsync<TResult>(
            TaskName name, object? input = null, TaskOptions? options = null)
        {
            TaskActivityScheduled message = new(
                this.cursor.GetNextId(),
                name,
                this.cursor.Converter.Serialize(input));
            return this.cursor.SendMessageAsync<TResult>(message);
        }

        public override Task<TResult> CallSubOrchestratorAsync<TResult>(
            TaskName orchestratorName, object? input = null, TaskOptions? options = null)
        {
            SubOrchestrationScheduled message = new(
                this.cursor.GetNextId(),
                orchestratorName,
                this.cursor.Converter.Serialize(input),
                new(this.InstanceId));
            return this.cursor.SendMessageAsync<TResult>(message);
        }

        public override void ContinueAsNew(object? newInput = null, bool preserveUnprocessedEvents = true)
        {
            throw new NotImplementedException();
        }

        public override Task CreateTimer(DateTime fireAt, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public override T GetInput<T>()
        {
            throw new NotImplementedException();
        }

        public override Guid NewGuid()
        {
            throw new NotImplementedException();
        }

        public override void SendEvent(string instanceId, string eventName, object payload)
        {
            throw new NotImplementedException();
        }

        public override void SetCustomStatus(object? customStatus)
        {
            this.workItem.CustomStatus = this.cursor.Converter.Serialize(customStatus);
        }

        public override Task<T> WaitForExternalEvent<T>(string eventName, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
