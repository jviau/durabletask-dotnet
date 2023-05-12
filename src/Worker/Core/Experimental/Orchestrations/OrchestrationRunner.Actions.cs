// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Runs orchestrations.
/// </summary>
partial class OrchestrationRunner
{
    public abstract record OrchestrationAction(int Id)
    {
        /// <summary>
        /// Gets a value indicating whether this action is fire and forget. Fire and forget actions will not be tracked
        /// for any results.
        /// </summary>
        public virtual bool FireAndForget => false;

        /// <summary>
        /// Checks for a match with an <see cref="OrchestrationMessage"/>.
        /// </summary>
        /// <param name="message">The message to check.</param>
        /// <returns>True if it matches, false otherwise.</returns>
        public abstract bool Matches(OrchestrationMessage message);

        /// <summary>
        /// Converts the action to the corresponding <see cref="OrchestrationMessage"/>.
        /// </summary>
        /// <param name="converter">The data converter. Null during replay, when conversion is not needed.</param>
        /// <returns>The orchestration message.</returns>
        public abstract OrchestrationMessage ToMessage(DataConverter converter);
    }

    public record TimerCreatedAction(int Id, DateTimeOffset FireAt)
        : OrchestrationAction(Id)
    {
        /// <inheritdoc/>
        public override bool Matches(OrchestrationMessage message)
        {
            return message is TimerScheduled created
                && this.Id == created.Id
                && this.FireAt == created.FireAt;
        }

        /// <inheritdoc/>
        public override OrchestrationMessage ToMessage(DataConverter converter)
        {
            return new TimerScheduled(this.Id, DateTimeOffset.UtcNow, this.FireAt);
        }
    }

    public record EventSentAction(int Id, string InstanceId, string Name, object? Input)
        : OrchestrationAction(Id)
    {
        /// <inheritdoc/>
        public override bool FireAndForget => true;

        /// <inheritdoc/>
        public override bool Matches(OrchestrationMessage message)
        {
            return message is EventSent sent
                && this.Id == sent.Id
                && this.InstanceId == sent.InstanceId
                && StringComparer.Ordinal.Equals(this.Name, sent.Name);
        }

        /// <inheritdoc/>
        public override OrchestrationMessage ToMessage(DataConverter converter)
        {
            return new EventSent(
                this.Id, DateTimeOffset.UtcNow, this.InstanceId, this.Name, converter.Serialize(this.Input));
        }
    }

    public record TaskActivityScheduledAction(int Id, TaskName Name, object? Input)
        : OrchestrationAction(Id)
    {
        /// <inheritdoc/>
        public override bool Matches(OrchestrationMessage message)
        {
            return message is TaskActivityScheduled scheduled
                && this.Id == scheduled.Id
                && this.Name == scheduled.Name;
        }

        /// <inheritdoc/>
        public override OrchestrationMessage ToMessage(DataConverter converter)
        {
            return new TaskActivityScheduled(
                this.Id, DateTimeOffset.UtcNow, this.Name, converter.Serialize(this.Input));
        }
    }

    public record SubOrchestrationScheduledAction(
        int Id, TaskName Name, object? Input, SubOrchestrationScheduledOptions? Options)
        : OrchestrationAction(Id)
    {
        /// <inheritdoc/>
        public override bool Matches(OrchestrationMessage message)
        {
            return message is SubOrchestrationScheduled scheduled
                && this.Id == scheduled.Id
                && this.Name == scheduled.Name;
        }

        /// <inheritdoc/>
        public override OrchestrationMessage ToMessage(DataConverter converter)
        {
            return new SubOrchestrationScheduled(
                this.Id, DateTimeOffset.UtcNow, this.Name, converter.Serialize(this.Input), this.Options);
        }
    }
}
