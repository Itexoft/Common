// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Concurrent;
using System.Diagnostics;
using Itexoft.IO.VFS.Diagnostics;
using Itexoft.IO.VFS.Metadata.Models;

namespace Itexoft.IO.VFS.Locking;

/// <summary>
/// Manages per-resource readers-writer locks, allowing the virtual file system to coordinate concurrent access with minimal contention.
/// </summary>
internal sealed class LockManager : IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private readonly ConcurrentDictionary<FileId, LockNode> nodes = new();
    private int disposed;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) != 0)
            return;

        foreach (var pair in this.nodes)
        {
            var node = pair.Value;
            node.MarkDisposed();
        }

        this.nodes.Clear();
    }

    /// <summary>
    /// Acquires a shared (reader) lock for the specified file identifier.
    /// </summary>
    /// <param name="fileId">Target file identifier.</param>
    /// <param name="timeout">Optional timeout for the acquisition.</param>
    /// <returns>A disposable lock handle.</returns>
    public LockHandle AcquireShared(FileId fileId, TimeSpan? timeout = null) =>
        this.Acquire(fileId, LockMode.Shared, timeout ?? DefaultTimeout);

    /// <summary>
    /// Acquires an exclusive (writer) lock for the specified file identifier.
    /// </summary>
    /// <param name="fileId">Target file identifier.</param>
    /// <param name="timeout">Optional timeout for the acquisition.</param>
    /// <returns>A disposable lock handle.</returns>
    public LockHandle AcquireExclusive(FileId fileId, TimeSpan? timeout = null) =>
        this.Acquire(fileId, LockMode.Exclusive, timeout ?? DefaultTimeout);

    private LockHandle Acquire(FileId fileId, LockMode mode, TimeSpan timeout)
    {
        this.ThrowIfDisposed();
        var milliseconds = ToMilliseconds(timeout);
        var node = this.nodes.GetOrAdd(fileId, static _ => new());

        if (!node.TryAcquire(mode, milliseconds))
            throw new IOException($"Timed out acquiring {mode} lock for file {fileId.Value} after {timeout}.");

        return new(this, fileId, node, mode);
    }

    internal void Release(FileId fileId, LockNode node, LockMode mode)
    {
        if (mode == LockMode.Shared)
            node.ReleaseShared();
        else
            node.ReleaseExclusive();

        // Do not dispose the node here: other threads may still hold references returned by GetOrAdd.
        // Nodes are cleaned up when the manager itself is disposed.
    }

    private static int ToMilliseconds(TimeSpan timeout)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
            return Timeout.Infinite;

        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout cannot be negative.");
        var ms = timeout.TotalMilliseconds;

        if (ms >= int.MaxValue)
            return Timeout.Infinite;

        return (int)ms;
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref this.disposed) != 0)
            throw new ObjectDisposedException(nameof(LockManager));
    }

    internal enum LockMode
    {
        Shared,
        Exclusive
    }

    internal struct LockHandle : IDisposable
    {
        private readonly LockManager manager;
        private readonly FileId fileId;
        private readonly LockNode node;
        private readonly LockMode mode;
        private int released;

        /// <summary>
        /// Initializes a new instance of the <see cref="LockHandle" /> struct.
        /// </summary>
        /// <param name="manager">Owning manager.</param>
        /// <param name="fileId">File identifier the lock references.</param>
        /// <param name="node">Underlying lock node.</param>
        /// <param name="mode">Lock mode requested.</param>
        public LockHandle(LockManager manager, FileId fileId, LockNode node, LockMode mode)
        {
            this.manager = manager;
            this.fileId = fileId;
            this.node = node;
            this.mode = mode;
            this.released = 0;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.released, 1) == 0)
                this.manager.Release(this.fileId, this.node, this.mode);
        }
    }

    internal sealed class LockNode : IDisposable
    {
        private readonly ReaderWriterLockSlim gate = new(LockRecursionPolicy.SupportsRecursion);
#if DEBUG
        private StackTrace? capturedStack;
#endif
        private volatile bool disposed;
        private volatile bool disposing;

        internal bool IsFree => this.gate.CurrentReadCount == 0 && !this.gate.IsWriteLockHeld && !this.gate.IsUpgradeableReadLockHeld;

        /// <inheritdoc />
        public void Dispose() => this.MarkDisposed();

        internal bool TryAcquire(LockMode mode, int millisecondsTimeout)
        {
            if (this.disposing)
                throw new ObjectDisposedException(nameof(LockNode), "Lock has been disposed.");

            return mode switch
            {
                LockMode.Shared => this.gate.TryEnterReadLock(millisecondsTimeout),
                LockMode.Exclusive => this.gate.TryEnterWriteLock(millisecondsTimeout),
                _ => false
            };
        }

        internal void ReleaseShared()
        {
            if (this.disposed)
                return;
            this.gate.ExitReadLock();
            this.TryFinalize();
        }

        internal void ReleaseExclusive()
        {
            if (this.disposed)
                return;
            this.gate.ExitWriteLock();
            this.TryFinalize();
        }

        internal void MarkDisposed()
        {
            if (this.disposing)
                return;

            this.disposing = true;
            this.TryFinalize();
#if DEBUG
            if (!this.disposed && !this.IsFree)
            {
                this.capturedStack ??= new(1, true);
                DebugUtility.Log(
                    $"[LockManager] LockNode marked disposed while still held. Owners: read={this.gate.CurrentReadCount} write={this.gate.IsWriteLockHeld} upgrade={this.gate.IsUpgradeableReadLockHeld}. Stack:\n{this.capturedStack}");
            }
#endif
        }

        private void TryFinalize()
        {
            if (!this.disposing || this.disposed || !this.IsFree)
                return;

            this.gate.Dispose();
            this.disposed = true;
        }
    }
}