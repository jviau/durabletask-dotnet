// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Runs orchestrations.
/// </summary>
partial class OrchestrationRunner
{
    class Context : TaskOrchestrationContext
    {
        readonly Cursor cursor;
        int newGuidCounter;

        public Context(Cursor cursor)
        {
            this.cursor = cursor;
        }

        /// <inheritdoc/>
        public override TaskName Name => this.cursor.WorkItem.Name;

        /// <inheritdoc/>
        public override string InstanceId => this.cursor.WorkItem.Id;

        /// <inheritdoc/>
        public override ParentOrchestrationInstance? Parent => this.cursor.WorkItem.Parent;

        public override DateTime CurrentUtcDateTime => this.cursor.CurrentDateTime.UtcDateTime;

        public override bool IsReplaying => this.cursor.WorkItem.IsReplaying;

        protected override ILoggerFactory LoggerFactory => this.cursor.LoggerFactory;

        public override Task<TResult> CallActivityAsync<TResult>(
            TaskName name, object? input = null, TaskOptions? options = null)
        {
            return this.RunActionAsync<TResult>(
                new TaskActivityScheduledAction(this.cursor.GetNextId(), name, input), options);
        }

        public override Task<TResult> CallSubOrchestratorAsync<TResult>(
            TaskName orchestratorName, object? input = null, TaskOptions? options = null)
        {
            string? instanceId = (options as SubOrchestrationOptions)?.InstanceId;
            SubOrchestrationScheduledAction action = new(
                this.cursor.GetNextId(), orchestratorName, input, new(instanceId));
            return this.RunActionAsync<TResult>(action, options);
        }

        public override void ContinueAsNew(object? newInput = null, bool preserveUnprocessedEvents = true)
        {
            this.cursor.ContinueAsNew(newInput, preserveUnprocessedEvents);
        }

        public override async Task CreateTimer(DateTime fireAt, CancellationToken cancellationToken)
        {
            // Make sure we're always operating in UTC
            DateTime finalFireAtUtc = fireAt.ToUniversalTime();

            // Longer timers are broken down into smaller timers. For example, if fireAt is 7 days from now
            // and the max interval is 3 days, there will be two 3-day timers and a single one-day timer.
            // This is primarily to support backends that don't support infinite timers, like Azure Storage.
            TimeSpan maximumTimerInterval = this.cursor.Options.MaximumTimerInterval;
            TimeSpan remainingTime = finalFireAtUtc.Subtract(this.CurrentUtcDateTime);
            while (remainingTime > maximumTimerInterval && !cancellationToken.IsCancellationRequested)
            {
                DateTime nextFireAt = this.CurrentUtcDateTime.Add(maximumTimerInterval);
                await this.cursor.RunActionAsync(new TimerCreatedAction(this.cursor.GetNextId(), nextFireAt));
                remainingTime = finalFireAtUtc.Subtract(this.CurrentUtcDateTime);
            }

            await this.cursor.RunActionAsync(new TimerCreatedAction(this.cursor.GetNextId(), finalFireAtUtc));
        }

        public override T GetInput<T>()
        {
            if (this.cursor.Input is null)
            {
                return default!;
            }

            return (T)this.cursor.Input;
        }

        public override Guid NewGuid()
        {
            static void SwapByteArrayValues(byte[] byteArray)
            {
                SwapByteArrayElements(byteArray, 0, 3);
                SwapByteArrayElements(byteArray, 1, 2);
                SwapByteArrayElements(byteArray, 4, 5);
                SwapByteArrayElements(byteArray, 6, 7);
            }

            static void SwapByteArrayElements(byte[] byteArray, int left, int right)
            {
                (byteArray[right], byteArray[left]) = (byteArray[left], byteArray[right]);
            }

            const string DnsNamespaceValue = "9e952958-5e33-4daf-827f-2fa12937b875";
            const int DeterministicGuidVersion = 5;

            Guid namespaceValueGuid = Guid.Parse(DnsNamespaceValue);

            // The name is a combination of the instance ID, the current orchestrator date/time, and a counter.
            string guidNameValue = string.Concat(
                this.InstanceId,
                "_",
                this.CurrentUtcDateTime.ToString("o"),
                "_",
                this.newGuidCounter.ToString(CultureInfo.InvariantCulture));

            this.newGuidCounter++;

            byte[] nameByteArray = Encoding.UTF8.GetBytes(guidNameValue);
            byte[] namespaceValueByteArray = namespaceValueGuid.ToByteArray();
            SwapByteArrayValues(namespaceValueByteArray);

            byte[] hashByteArray;
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms -- not for cryptography
            using (HashAlgorithm hashAlgorithm = SHA1.Create())
            {
                hashAlgorithm.TransformBlock(namespaceValueByteArray, 0, namespaceValueByteArray.Length, null, 0);
                hashAlgorithm.TransformFinalBlock(nameByteArray, 0, nameByteArray.Length);
                hashByteArray = hashAlgorithm.Hash;
            }
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms -- not for cryptography

            byte[] newGuidByteArray = new byte[16];
            Array.Copy(hashByteArray, 0, newGuidByteArray, 0, 16);

            int versionValue = DeterministicGuidVersion;
            newGuidByteArray[6] = (byte)((newGuidByteArray[6] & 0x0F) | (versionValue << 4));
            newGuidByteArray[8] = (byte)((newGuidByteArray[8] & 0x3F) | 0x80);

            SwapByteArrayValues(newGuidByteArray);

            return new Guid(newGuidByteArray);
        }

        public override void SendEvent(string instanceId, string eventName, object payload)
        {
            EventSentAction action = new(this.cursor.GetNextId(), instanceId, eventName, payload);
            this.cursor.RunActionAsync(action).GetAwaiter().GetResult(); // TODO: make method async
        }

        public override void SetCustomStatus(object? customStatus) => this.cursor.SetCustomStatus(customStatus);

        public override Task<T> WaitForExternalEvent<T>(string eventName, CancellationToken cancellationToken = default)
        {
            return this.cursor.WaitExternalEventAsync<T>(eventName, cancellationToken);
        }

        Task<T> RunActionAsync<T>(OrchestrationAction action, TaskOptions? options)
        {
            static Func<CancellationToken, Task<T>> CreateCallback(Cursor cursor, OrchestrationAction action)
            {
                bool first = true;
                return _ =>
                {
                    if (!first)
                    {
                        action = action with { Id = cursor.GetNextId() };
                    }

                    first = false;
                    return cursor.RunActionAsync<T>(action);
                };
            }

            return options switch
            {
                { Retry: { } retry } => retry.InvokeAsync(this, CreateCallback(this.cursor, action), default),
                _ => this.cursor.RunActionAsync<T>(action),
            };
        }
    }
}
