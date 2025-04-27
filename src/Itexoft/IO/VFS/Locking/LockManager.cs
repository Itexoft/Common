// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Concurrent;
using Itexoft.Core;
using Itexoft.IO.VFS.Metadata.Models;

namespace Itexoft.IO.VFS.Locking;

/// <summary>
/// Manages per-resource readers-writer locks, allowing the virtual file system to coordinate concurrent access with minimal contention.
/// </summary>
internal sealed class LockManager : IDisposable
{
    private static readonly TimeSpan defaultTimeout = TimeSpan.FromSeconds(5);
    private readonly ConcurrentDictionary<FileId, LockNode> nodes = new();
    private Disposed disposed;

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        foreach (var pair in this.nodes)
            pair.Value.Dispose();

        this.nodes.Clear();
    }

    /// <summary>
    /// Acquires a shared (reader) lock for the specified file identifier.
    /// </summary>
    /// <param name="fileId">Target file identifier.</param>
    /// <param name="timeout">Optional timeout for the acquisition.</param>
    /// <returns>A disposable lock handle.</returns>
    public LockHandle AcquireShared(FileId fileId, TimeSpan? timeout = null) =>
        this.Acquire(fileId, LockMode.Shared, timeout ?? defaultTimeout);

    /// <summary>
    /// Acquires an exclusive (writer) lock for the specified file identifier.
    /// </summary>
    /// <param name="fileId">Target file identifier.</param>
    /// <param name="timeout">Optional timeout for the acquisition.</param>
    /// <returns>A disposable lock handle.</returns>
    public LockHandle AcquireExclusive(FileId fileId, TimeSpan? timeout = null) =>
        this.Acquire(fileId, LockMode.Exclusive, timeout ?? defaultTimeout);

    private LockHandle Acquire(FileId fileId, LockMode mode, TimeSpan timeout)
    {
        this.disposed.ThrowIf();
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

    internal enum LockMode
    {
        Shared,
        Exclusive,
    }

    internal struct LockHandle : IDisposable
    {
        private readonly LockManager manager;
        private readonly FileId fileId;
        private readonly LockNode node;
        private readonly LockMode mode;
        private Disposed disposed;

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
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (this.disposed.Enter())
                return;

            this.manager.Release(this.fileId, this.node, this.mode);
        }
    }

    internal sealed class LockNode : IDisposable
    {
        private readonly ReaderWriterLockSlim gate = new(LockRecursionPolicy.SupportsRecursion);
        private Disposed disposed;

        public void Dispose()
        {
            if (this.disposed.Enter())
                return;

            this.gate.Dispose();
        }

        internal bool TryAcquire(LockMode mode, int millisecondsTimeout)
        {
            this.disposed.ThrowIf();

            return mode switch
            {
                LockMode.Shared => this.gate.TryEnterReadLock(millisecondsTimeout),
                LockMode.Exclusive => this.gate.TryEnterWriteLock(millisecondsTimeout),
                _ => false,
            };
        }

        internal void ReleaseShared()
        {
            this.disposed.ThrowIf();
            this.gate.ExitReadLock();
        }

        internal void ReleaseExclusive()
        {
            this.disposed.ThrowIf();
            this.gate.ExitWriteLock();
        }
    }
}
