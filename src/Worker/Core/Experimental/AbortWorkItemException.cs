// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.Serialization;

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// A work item has aborted execution. The results should not be persisted and it should be retried later.
/// </summary>
public class AbortWorkItemException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AbortWorkItemException"/> class.
    /// </summary>
    public AbortWorkItemException()
        : base("The work item execution has been aborted.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AbortWorkItemException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public AbortWorkItemException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AbortWorkItemException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public AbortWorkItemException(string message, Exception innerException)
            : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AbortWorkItemException"/> class.
    /// </summary>
    /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
    /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
    protected AbortWorkItemException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
}
