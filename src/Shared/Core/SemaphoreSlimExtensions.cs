// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask;

/// <summary>
/// A lock for a semaphore.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Not used.")]
struct SemaphoreLock : IDisposable
{
    SemaphoreSlim? semaphore;

    /// <summary>
    /// Initializes a new instance of the <see cref="SemaphoreLock"/> struct.
    /// </summary>
    /// <param name="semaphore">The semaphore this lock is for.</param>
    public SemaphoreLock(SemaphoreSlim semaphore)
    {
        this.semaphore = Check.NotNull(semaphore);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        SemaphoreSlim? semaphore = Interlocked.Exchange(ref this.semaphore, null);
        semaphore?.Release();
    }
}

/// <summary>
/// Extensions for <see cref="SemaphoreSlim" />.
/// </summary>
static class SemaphoreSlimExtensions
{
    /// <summary>
    /// Waits for a semaphore, returning a disposable that releases the semaphore on dispose. Do NOT pass this
    /// disposable around as it is a struct and can result in multi-release.
    /// </summary>
    /// <param name="semaphore">The semaphore to lock.</param>
    /// <param name="cancellationToken">The cancellation token. Only used while waiting for the semaphore.</param>
    /// <returns>A lock for this semaphore.</returns>
    public static async Task<SemaphoreLock> LockAsync(
        this SemaphoreSlim semaphore, CancellationToken cancellationToken = default)
    {
        Check.NotNull(semaphore);

        await semaphore.WaitAsync(cancellationToken);
        return new SemaphoreLock(semaphore);
    }
}
