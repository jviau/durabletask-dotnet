// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Worker;

/// <summary>
/// Represents a lockable item.
/// </summary>
public interface ILockable
{
    /// <summary>
    /// Tries to renew the lock.
    /// </summary>
    /// <param name="cancellation">The cancellation token.</param>
    /// <returns>A <see cref="LockResult"/> indicating if lock renewal succeeded or not.</returns>
    ValueTask<LockResult> TryRenewLockAsync(CancellationToken cancellation = default);
}

/// <summary>
/// Represents a lock result.
/// </summary>
public readonly struct LockResult
{
    LockResult(DateTimeOffset expiry)
    {
        this.Success = true;
        this.ExpiresAt = expiry;
    }

    /// <summary>
    /// Gets a value indicating whether the lock acquiring was successful.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Gets the time when the lock will expire.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>
    /// Gets a <see cref="LockResult"/> indicating successful lock acquisition.
    /// </summary>
    /// <param name="expiry">The time when the lock will expire.</param>
    /// <returns>A <see cref="LockResult"/>.</returns>
    public static LockResult Succeeded(DateTimeOffset expiry) => new(expiry);

    /// <summary>
    /// Gets a <see cref="LockResult"/> indicating failed lock acquisition.
    /// </summary>
    /// <returns>A <see cref="LockResult"/>.</returns>
    public static LockResult Failed() => default;
}
