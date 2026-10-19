namespace System.Threading;

/// <summary>
/// ValueTask returning extension methods for SemaphoreSlim's .WaitAsync methods.
/// Returns a Task-free ValueTask when the semaphore is immediately acquirable, otherwise returns a ValueTask wrapping
/// the Task returned by SemaphoreSlim.WaitAsync.
/// </summary>
/// <remarks>
/// Note that this is presently only optimized for the case when the semaphore is immediately acquirable.
/// Invalid timeouts or an already canceled CancellationToken will result in a ValueTask wrapped Task.
/// 
/// Under very high contention, when the semaphore is usually unavailable, this might be less efficient, but generally,
/// under most circumstances, this will be more efficient than the standard SemaphoreSlim.WaitAsync methods, because
/// it avoids allocating a Task object when the semaphore is available.
/// 
/// Potential future improvements:
/// * Synchronously handle the already canceled CancellationToken case.
/// * Synchronously handle the invalid timeout value cases.
/// It would be important to match the exact behavior of the standard SemaphoreSlim.WaitAsync methods.
/// </remarks>
public static class SemaphoreSlimWaitValueAsyncExtensions {
    private static readonly TimeSpan TimeoutLimit = TimeSpan.FromMilliseconds(int.MaxValue + 1L); // must be less than this

    extension(SemaphoreSlim semaphore) {
        /// <summary>
        /// Asynchronously waits to enter the <see cref="SemaphoreSlim"/>.
        /// </summary>
        /// <returns>A task that will complete when the semaphore has been entered.</returns>
        public ValueTask WaitValueAsync() =>
            semaphore.Wait(0) // try to acquire the semaphore synchronously
                ? ValueTask.CompletedTask
                : new ValueTask(semaphore.WaitAsync());

        /// <summary>
        /// Asynchronously waits to enter the <see cref="SemaphoreSlim"/>, using a <see
        /// cref="TimeSpan"/> to measure the time interval, while observing a
        /// <see cref="CancellationToken"/>.
        /// </summary>
        /// <param name="timeout">
        /// A <see cref="TimeSpan"/> that represents the amount of time
        /// to wait, or a <see cref="Timeout.InfiniteTimeSpan"/> to wait indefinitely.
        /// </param>
        /// <returns>
        /// A task that will complete with a result of true if the current thread successfully entered
        /// the <see cref="SemaphoreSlim"/>, otherwise with a result of false.
        /// </returns>
        /// <exception cref="ObjectDisposedException">
        /// The current instance has already been disposed.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="timeout"/> is a negative TimeSpan other than -1 milliseconds, which represents
        /// an infinite time-out -or- timeout is greater than or equal to <see cref="int.MaxValue"/>+1.
        /// </exception>
        public ValueTask<bool> WaitValueAsync(TimeSpan timeout) =>
            (timeout == Timeout.InfiniteTimeSpan || timeout >= TimeSpan.Zero && timeout < TimeoutLimit) &&
            semaphore.Wait(0)
                ? ValueTask.FromResult(true)
                : new ValueTask<bool>(semaphore.WaitAsync(timeout));

        /// <summary>
        /// Asynchronously waits to enter the <see cref="SemaphoreSlim"/>,
        /// using a 32-bit signed integer to measure the time interval.
        /// </summary>
        /// <param name="millisecondsTimeout">
        /// The number of milliseconds to wait, or -1 to wait indefinitely.
        /// </param>
        /// <returns>
        /// A task that will complete with a result of true if the current thread successfully entered
        /// the <see cref="SemaphoreSlim"/>, otherwise with a result of false.
        /// </returns>
        /// <exception cref="ObjectDisposedException">The current instance has already been
        /// disposed.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="millisecondsTimeout"/> is a negative number other than -1,
        /// which represents an infinite time-out.
        /// </exception>
        public ValueTask<bool> WaitValueAsync(int millisecondsTimeout) =>
            millisecondsTimeout >= -1 && // -1 is the value for Timeout.Infinite, which is valid.
            semaphore.Wait(0)
                ? ValueTask.FromResult(true)
                : new ValueTask<bool>(semaphore.WaitAsync(millisecondsTimeout));

        /// <summary>
        /// Asynchronously waits to enter the <see cref="SemaphoreSlim"/>, while observing a
        /// <see cref="CancellationToken"/>.
        /// </summary>
        /// <returns>A task that will complete when the semaphore has been entered.</returns>
        /// <param name="cancellationToken">
        /// The <see cref="CancellationToken"/> token to observe.
        /// </param>
        /// <exception cref="ObjectDisposedException">
        /// The current instance has already been disposed.
        /// </exception>
        public ValueTask WaitValueAsync(CancellationToken cancellationToken) =>
            !cancellationToken.IsCancellationRequested &&
            semaphore.Wait(0)
                ? ValueTask.CompletedTask
                : new ValueTask(semaphore.WaitAsync(cancellationToken));

        /// <summary>
        /// Asynchronously waits to enter the <see cref="SemaphoreSlim"/>, using a <see
        /// cref="TimeSpan"/> to measure the time interval.
        /// </summary>
        /// <param name="timeout">
        /// A <see cref="TimeSpan"/> that represents the amount of time
        /// to wait, or a <see cref="Timeout.InfiniteTimeSpan"/> to wait indefinitely.
        /// </param>
        /// <param name="cancellationToken">
        /// The <see cref="CancellationToken"/> token to observe.
        /// </param>
        /// <returns>
        /// A task that will complete with a result of true if the current thread successfully entered
        /// the <see cref="SemaphoreSlim"/>, otherwise with a result of false.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="timeout"/> is a negative TimeSpan other than -1 milliseconds, which represents
        /// an infinite time-out -or- timeout is greater than or equal to <see cref="int.MaxValue"/>+1.
        /// </exception>
        public ValueTask<bool> WaitValueAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            !cancellationToken.IsCancellationRequested &&
            (timeout == Timeout.InfiniteTimeSpan || timeout >= TimeSpan.Zero && timeout < TimeoutLimit) &&
            semaphore.Wait(0)
                ? ValueTask.FromResult(true)
                : new ValueTask<bool>(semaphore.WaitAsync(timeout, cancellationToken));

        /// <summary>
        /// Asynchronously waits to enter the <see cref="SemaphoreSlim"/>,
        /// using a 32-bit signed integer to measure the time interval,
        /// while observing a <see cref="CancellationToken"/>.
        /// </summary>
        /// <param name="millisecondsTimeout">
        /// The number of milliseconds to wait, or -1 to wait indefinitely.
        /// </param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to observe.</param>
        /// <returns>
        /// A task that will complete with a result of true if the current thread successfully entered
        /// the <see cref="SemaphoreSlim"/>, otherwise with a result of false.
        /// </returns>
        /// <exception cref="ObjectDisposedException">The current instance has already been
        /// disposed.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="millisecondsTimeout"/> is a negative number other than -1,
        /// which represents an infinite time-out.
        /// </exception>
        public ValueTask<bool> WaitValueAsync(int millisecondsTimeout, CancellationToken cancellationToken) =>
            !cancellationToken.IsCancellationRequested &&
            millisecondsTimeout >= -1 && // -1 is the value for Timeout.Infinite, which is valid.
            semaphore.Wait(0)
                ? ValueTask.FromResult(true)
                : new ValueTask<bool>(semaphore.WaitAsync(millisecondsTimeout, cancellationToken));
    }
}
