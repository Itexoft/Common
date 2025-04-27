// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Collections.Concurrent;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.IO.VFS.Allocation;
using Itexoft.IO.VFS.Core;
using Itexoft.IO.VFS.FileSystem;
using Itexoft.IO.VFS.Infrastructure;
using Itexoft.IO.VFS.Locking;
using Itexoft.IO.VFS.Metadata;
using Itexoft.IO.VFS.Metadata.Attributes;
using Itexoft.IO.VFS.Metadata.Models;
using Itexoft.IO.VFS.Storage;

namespace Itexoft.IO.VFS;

/// <summary>
/// High-level API that exposes a hierarchical virtual file system backed by a single seekable stream.
/// </summary>
public sealed class VirtualFileSystem : IDisposable
{
    private static readonly char[] pathSeparators = ['/', '\\'];
    private readonly AttributeTable attributeTable;
    private readonly CompactionEngine? compactionEngine;
    private readonly DirectoryIndex directoryIndex;
    private readonly ExtentAllocator extentAllocator;
    private readonly FileTable fileTable;
    private readonly LockManager lockManager;
    private readonly MetadataPersistence metadataPersistence;
    private readonly ConcurrentDictionary<VirtualFileStream, byte> openStreams = new();
    private readonly ServiceRegistry services = new();
    private readonly StorageEngine storage;
    private Disposed disposed;

    private VirtualFileSystem(Stream baseStream, VirtualFileSystemOptions options)
    {
        if (!baseStream.CanRead || !baseStream.CanWrite || !baseStream.CanSeek)
            throw new ArgumentException("Base stream must support read/write/seek.", nameof(baseStream));

        this.UnderlyingStream = baseStream;

        SuperblockInspector.Inspect(baseStream, out _, out var detectedPageSize, out var hasValidSuperblock);

        var existingPageSize = hasValidSuperblock ? detectedPageSize : (int?)null;

        if (options.PageSize.HasValue)
        {
            var requestedPageSize = PageSizing.Normalize(options.PageSize);

            if (existingPageSize.HasValue && existingPageSize.Value != requestedPageSize)
            {
                throw new InvalidOperationException(
                    $"Requested page size {requestedPageSize} does not match existing page size {existingPageSize.Value}.");
            }

            this.PageSize = requestedPageSize;
        }
        else
            this.PageSize = PageSizing.Normalize(existingPageSize);

        StorageEngine storage;
        Stream? mirrorStream = null;

        try
        {
            if (options.EnableMirroring)
            {
                if (baseStream is not FileStream primaryFile)
                    throw new NotSupportedException("Mirroring requires the base stream to be a FileStream.");

                mirrorStream = PrepareMirrorStream(primaryFile);
                storage = StorageEngine.OpenMirrored(baseStream, mirrorStream, this.PageSize, true);
            }
            else
                storage = StorageEngine.Open(baseStream, this.PageSize);
        }
        catch
        {
            mirrorStream?.Dispose();

            throw;
        }

        this.storage = storage;
        this.services.Add(this.UnderlyingStream);
        this.services.Add(this.storage);
        this.services.Add(new ExtentAllocator(this.storage));
        this.services.Add(new FileTable(this.RequireService<ExtentAllocator>()));
        this.services.Add(new DirectoryIndex(this.RequireService<FileTable>()));
        this.services.Add(new AttributeTable(this.RequireService<FileTable>()));
        this.services.Add(new LockManager());
        this.extentAllocator = this.RequireService<ExtentAllocator>();
        this.fileTable = this.RequireService<FileTable>();
        this.directoryIndex = this.RequireService<DirectoryIndex>();
        this.attributeTable = this.RequireService<AttributeTable>();
        this.lockManager = this.RequireService<LockManager>();

        this.metadataPersistence = new(this.storage, this.extentAllocator, this.fileTable, this.directoryIndex, this.attributeTable);
        this.metadataPersistence.Load();

        if (options.EnableCompaction)
        {
            this.compactionEngine = new(
                this.storage,
                this.extentAllocator,
                this.fileTable,
                this.directoryIndex,
                this.metadataPersistence,
                this.lockManager,
                this.PageSize);

            this.compactionEngine.TriggerFullScan();
        }
        else
            this.compactionEngine = null;
    }

    internal Stream UnderlyingStream { get; }

    internal int PageSize { get; }

    internal byte ActiveSuperblockSlot => this.storage.ActiveSlot;

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        this.compactionEngine?.Dispose();
        this.DisposeOpenStreams();
        this.metadataPersistence.Flush();
        this.storage.Dispose();
        this.lockManager.Dispose();
        this.UnderlyingStream.Flush();
    }

    private void UnregisterStream(VirtualFileStream stream) => this.openStreams.TryRemove(stream, out _);

    private void DisposeOpenStreams()
    {
        foreach (var stream in this.openStreams.Keys)
            stream.Dispose();
    }

    /// <summary>
    /// Mounts a virtual file system over the provided stream.
    /// </summary>
    /// <param name="stream">Seekable stream containing the file-system image.</param>
    /// <param name="options">Optional configuration overrides.</param>
    /// <returns>A mounted <see cref="VirtualFileSystem" /> instance.</returns>
    public static VirtualFileSystem Mount(Stream stream, VirtualFileSystemOptions? options = null) =>
        new(stream, options ?? new VirtualFileSystemOptions());

    internal void RunCompaction() => this.compactionEngine?.RunOnce();
    internal FileMetadata GetFileMetadata(FileId fileId) => this.fileTable.Get(fileId);

    /// <summary>
    /// Opens a file located at <paramref name="path" /> using the specified mode and access.
    /// </summary>
    /// <param name="path">Path to the file within the virtual hierarchy.</param>
    /// <param name="mode">File open mode.</param>
    /// <param name="access">Desired access level.</param>
    /// <returns>A <see cref="Stream" /> representing the file.</returns>
    public Stream OpenFile(string path, FileMode mode, FileAccess access)
    {
        this.disposed.ThrowIf();
        path.Required();

        if (mode == FileMode.Append && access == FileAccess.Read)
            access = FileAccess.Write;

        var segments = SplitPath(path);

        if (segments.Length == 0)
            throw new ArgumentException("Path must include a file name.", nameof(path));

        var parentId = this.ResolveParentDirectory(segments);
        var fileName = segments[^1];

        DirectoryEntry? entry;
        var exists = this.TryResolveEntry(segments, out var resolvedParentId, out entry);

        if (exists)
            parentId = resolvedParentId;

        switch (mode)
        {
            case FileMode.CreateNew:
                if (exists)
                    throw new IOException($"File '{path}' already exists.");

                this.CreateFile(path);
                exists = this.TryResolveEntry(segments, out parentId, out entry);

                break;
            case FileMode.Create:
                if (exists)
                    this.DeleteFile(path);

                this.CreateFile(path);
                exists = this.TryResolveEntry(segments, out parentId, out entry);

                break;
            case FileMode.Open:
                if (!exists)
                    throw new FileNotFoundException($"File '{path}' not found.", path);

                break;
            case FileMode.OpenOrCreate:
                if (!exists)
                {
                    this.CreateFile(path);
                    exists = this.TryResolveEntry(segments, out parentId, out entry);
                }

                break;
            case FileMode.Truncate:
                if (!exists)
                    throw new FileNotFoundException($"File '{path}' not found.", path);

                break;
            case FileMode.Append:
                if (!exists)
                {
                    this.CreateFile(path);
                    exists = this.TryResolveEntry(segments, out parentId, out entry);
                }

                access = FileAccess.Write;

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (!exists || entry is null)
            throw new IOException($"Unable to resolve file '{path}'.");

        if (entry.Kind != FileKind.File)
            throw new IOException($"Entry '{path}' is not a file.");

        var fileId = entry.TargetId;
        var lockHandle = (access & FileAccess.Write) != 0 ? this.lockManager.AcquireExclusive(fileId) : this.lockManager.AcquireShared(fileId);

        var metadata = this.fileTable.Get(fileId);

        var stream = new VirtualFileStream(
            this.storage,
            this.extentAllocator,
            this.fileTable,
            this.directoryIndex,
            this.metadataPersistence,
            this.compactionEngine,
            lockHandle,
            fileId,
            parentId,
            fileName,
            access,
            mode,
            metadata,
            this.UnregisterStream);

        this.openStreams.TryAdd(stream, 0);

        return stream;
    }

#if DEBUG
    internal (long PrimaryGeneration, long MirrorGeneration) DescribeStorageGenerations() =>
        (this.storage.PrimaryGeneration, this.storage.MirrorGeneration);
#endif

    internal IEnumerable<string> EnumerateDebugFileMetadata()
    {
        foreach (var kvp in this.fileTable.Enumerate())
        {
            var spans = string.Join(", ", kvp.Value.Extents.Select(span => $"[{span.Start.Value}..{span.EndExclusive})"));

            yield return $"file {kvp.Key.Value}: length={kvp.Value.Length}, extents={spans}";
        }
    }

    internal IEnumerable<string> EnumerateDebugDirectory(string path)
    {
        var segments = SplitPath(path);
        var directoryId = segments.Length == 0 ? FileId.Root : this.GetDirectoryId(segments);

        foreach (var entry in this.directoryIndex.Enumerate(directoryId))
            yield return $"{entry.Name} -> {entry.TargetId.Value} ({entry.Kind})";
    }

    internal FileId ResolveDebugFileId(string path)
    {
        var segments = SplitPath(path);

        return this.TryResolveEntry(segments, out _, out var entry) && entry is not null ? entry.TargetId : FileId.Invalid;
    }

    internal Dictionary<long, FileId> CaptureDebugPageOwners()
    {
        var map = new Dictionary<long, FileId>();

        foreach (var kvp in this.fileTable.Enumerate())
        foreach (var extent in kvp.Value.Extents)
        {
            var end = extent.Start.Value + extent.Length;

            for (var page = extent.Start.Value; page < end; page++)
                map[page] = kvp.Key;
        }

        return map;
    }

    internal IEnumerable<(long Page, List<long> Files)> FindDebugDuplicatePages()
    {
        var buckets = new Dictionary<long, List<long>>();

        foreach (var kvp in this.fileTable.Enumerate())
        foreach (var extent in kvp.Value.Extents)
        {
            var end = extent.Start.Value + extent.Length;

            for (var page = extent.Start.Value; page < end; page++)
            {
                if (!buckets.TryGetValue(page, out var list))
                {
                    list = [];
                    buckets[page] = list;
                }

                list.Add(kvp.Key.Value);
            }
        }

        foreach (var kvp in buckets)
        {
            if (kvp.Value.Count > 1)
                yield return (kvp.Key, kvp.Value);
        }
    }

    internal string DescribeDebugUsage()
    {
        var totalPages = this.extentAllocator.DebugTotalPages;
        var owners = this.CaptureDebugPageOwners();
        var metadata = this.metadataPersistence.CaptureDebugMetadata();

        var metadataPages = new HashSet<long>();

        void addSpan(PageSpan span)
        {
            if (!span.IsValid)
                return;

            var end = span.Start.Value + span.Length;

            for (var page = span.Start.Value; page < end; page++)
                metadataPages.Add(page);
        }

        addSpan(metadata.FileTable);
        addSpan(metadata.DirectoryIndex);
        addSpan(metadata.AttributeTable);

        var usedData = owners.Count;
        var usedMeta = metadataPages.Count;
        const int reservedSuperblock = 2;
        var unused = totalPages - usedData - usedMeta - reservedSuperblock;

        var metadataSnapshot = this.metadataPersistence.CaptureDebugMetadata();

        static string formatSpan(PageSpan span) => span.IsValid ? $"[{span.Start.Value}..{span.EndExclusive})" : "<none>";

        return $"pages: total={
            totalPages
        }, data={
            usedData
        }, metadata={
            usedMeta
        }, superblock={
            reservedSuperblock
        }, remaining={
            unused
        }, metaSpans={{File:{
            formatSpan(metadataSnapshot.FileTable)
        },Dir:{
            formatSpan(metadataSnapshot.DirectoryIndex)
        },Attr:{
            formatSpan(metadataSnapshot.AttributeTable)
        }}}";
    }

    internal byte[] DebugReadPage(long pageNumber)
    {
        var buffer = new byte[this.PageSize];
        this.storage.ReadPage(new(pageNumber), buffer);

        return buffer;
    }

    /// <summary>
    /// Creates a directory at the specified path.
    /// </summary>
    /// <param name="path">Directory path.</param>
    public void CreateDirectory(string path)
    {
        this.disposed.ThrowIf();

        path.Required();
        var segments = SplitPath(path);
        var current = FileId.Root;

        if (segments.Length == 0)
            return;

        var changed = false;

        foreach (var segment in segments)
        {
            using var handle = this.lockManager.AcquireExclusive(current);

            if (this.directoryIndex.TryGet(current, segment, out var entry))
            {
                if (entry.Kind != FileKind.Directory)
                    throw new IOException($"Path segment '{segment}' is not a directory.");

                current = entry.TargetId;

                continue;
            }

            current = this.CreateDirectoryInternal(current, segment);
            changed = true;
        }

        if (changed)
            this.metadataPersistence.Flush();
    }

    /// <summary>
    /// Creates a file at the specified path with optional attributes.
    /// </summary>
    /// <param name="path">File path.</param>
    /// <param name="attributes">File attributes to assign.</param>
    public void CreateFile(string path, FileAttributes attributes = FileAttributes.Normal)
    {
        this.disposed.ThrowIf();

        path.Required();
        var segments = SplitPath(path);

        if (segments.Length == 0)
            throw new ArgumentException("Path must include a file name.", nameof(path));

        var parent = this.ResolveParentDirectory(segments);
        var name = segments[^1];

        using var handle = this.lockManager.AcquireExclusive(parent);

        if (this.directoryIndex.TryGet(parent, name, out _))
            throw new IOException($"Entry '{name}' already exists.");

        var fileId = this.fileTable.Allocate(FileKind.File, attributes);
        var now = DateTime.UtcNow;

        var entry = new DirectoryEntry
        {
            Name = name,
            TargetId = fileId,
            Kind = FileKind.File,
            Attributes = attributes,
            CreatedUtc = now,
            ModifiedUtc = now,
            AccessedUtc = now,
            Generation = 0,
        };

        this.directoryIndex.Upsert(parent, name, entry);
        this.metadataPersistence.Flush();
        this.compactionEngine?.NotifyFileChanged(fileId);
    }

    /// <summary>
    /// Determines whether a file exists at the provided path.
    /// </summary>
    /// <param name="path">Path to check.</param>
    /// <returns><c>true</c> if the file exists; otherwise, <c>false</c>.</returns>
    public bool FileExists(string path)
    {
        this.disposed.ThrowIf();

        path.Required();
        var segments = SplitPath(path);

        if (segments.Length == 0)
            return false;

        var parent = this.ResolveParentDirectory(segments);
        var name = segments[^1];

        using var handle = this.lockManager.AcquireShared(parent);

        return this.directoryIndex.TryGet(parent, name, out var entry) && entry is { Kind: FileKind.File };
    }

    /// <summary>
    /// Determines whether a directory exists at the provided path.
    /// </summary>
    /// <param name="path">Path to check.</param>
    /// <returns><c>true</c> if the directory exists; otherwise, <c>false</c>.</returns>
    public bool DirectoryExists(string path)
    {
        this.disposed.ThrowIf();

        path.Required();
        var segments = SplitPath(path);

        if (segments.Length == 0)
            return true;

        var current = FileId.Root;

        foreach (var segment in segments)
        {
            using var handle = this.lockManager.AcquireShared(current);

            if (!this.directoryIndex.TryGet(current, segment, out var entry) || entry.Kind != FileKind.Directory)
                return false;

            current = entry.TargetId;
        }

        return true;
    }

    /// <summary>
    /// Deletes the file located at the specified path.
    /// </summary>
    /// <param name="path">Path to the file.</param>
    public void DeleteFile(string path)
    {
        this.disposed.ThrowIf();

        path.Required();
        var segments = SplitPath(path);

        if (segments.Length == 0)
            throw new ArgumentException("File path cannot be root.", nameof(path));

        var parent = this.ResolveParentDirectory(segments);
        var name = segments[^1];

        using var handle = this.lockManager.AcquireExclusive(parent);

        if (!this.directoryIndex.TryGet(parent, name, out var entry) || entry.Kind != FileKind.File)
            throw new FileNotFoundException($"File '{path}' not found.", path);

        this.DeleteFileEntry(parent, entry);
        this.metadataPersistence.Flush();
        this.compactionEngine?.NotifyFileChanged(entry.TargetId);
    }

    /// <summary>
    /// Deletes the directory at the specified path.
    /// </summary>
    /// <param name="path">Directory path.</param>
    /// <param name="recursive">Whether to delete contents recursively.</param>
    public void DeleteDirectory(string path, bool recursive = false)
    {
        this.disposed.ThrowIf();

        path.Required();
        var segments = SplitPath(path);

        if (segments.Length == 0)
            throw new IOException("Cannot delete the root directory.");

        var parent = this.ResolveParentDirectory(segments);
        var name = segments[^1];

        using var parentLock = this.lockManager.AcquireExclusive(parent);

        if (!this.directoryIndex.TryGet(parent, name, out var entry) || entry.Kind != FileKind.Directory)
            throw new DirectoryNotFoundException($"Directory '{path}' not found.");

        using var directoryLock = this.lockManager.AcquireExclusive(entry.TargetId);

        if (!recursive && this.directoryIndex.Enumerate(entry.TargetId).Any())
            throw new IOException("Directory is not empty.");

        if (recursive)
            this.DeleteDirectoryContents(entry.TargetId);

        this.directoryIndex.Remove(parent, name);
        this.fileTable.Remove(entry.TargetId);
        this.metadataPersistence.Flush();
        this.compactionEngine?.TriggerFullScan();
    }

    /// <summary>
    /// Enumerates names of entries located in the specified directory.
    /// </summary>
    /// <param name="path">Directory path.</param>
    /// <returns>A list containing entry names.</returns>
    public IReadOnlyList<string> EnumerateDirectory(string path)
    {
        this.disposed.ThrowIf();
        path.Required();
        var segments = SplitPath(path);
        var directoryId = segments.Length == 0 ? FileId.Root : this.GetDirectoryId(segments);
        using var handle = this.lockManager.AcquireShared(directoryId);

        return this.directoryIndex.Enumerate(directoryId).Select(entry => entry.Name).ToList();
    }

    /// <summary>
    /// Sets or replaces an extended attribute value for the given path.
    /// </summary>
    /// <param name="path">Target path.</param>
    /// <param name="attributeName">Name of the attribute.</param>
    /// <param name="value">Binary value to store.</param>
    public void SetAttribute(string path, string attributeName, ReadOnlySpan<byte> value)
    {
        this.disposed.ThrowIf();
        path.Required();
        ArgumentException.ThrowIfNullOrEmpty(attributeName);

        var segments = SplitPath(path);

        if (!this.TryResolveEntry(segments, out _, out var entry) || entry is null || entry.Kind != FileKind.File)
            throw new FileNotFoundException($"File '{path}' not found.", path);

        using var handle = this.lockManager.AcquireExclusive(entry.TargetId);
        this.attributeTable.Upsert(entry.TargetId, attributeName, value);
        this.metadataPersistence.Flush();
        this.compactionEngine?.NotifyFileChanged(entry.TargetId);
    }

    /// <summary>
    /// Attempts to retrieve an extended attribute.
    /// </summary>
    /// <param name="path">Target path.</param>
    /// <param name="attributeName">Attribute name.</param>
    /// <param name="value">When this method returns, contains the attribute value if found.</param>
    /// <returns><c>true</c> if the attribute exists; otherwise, <c>false</c>.</returns>
    public bool TryGetAttribute(string path, string attributeName, out byte[] value)
    {
        this.disposed.ThrowIf();
        path.Required();
        ArgumentException.ThrowIfNullOrEmpty(attributeName);

        var segments = SplitPath(path);

        if (!this.TryResolveEntry(segments, out _, out var entry) || entry is null || entry.Kind != FileKind.File)
        {
            value = [];

            return false;
        }

        using var handle = this.lockManager.AcquireShared(entry.TargetId);

        if (this.attributeTable.TryGet(entry.TargetId, attributeName, out var record) && record is not null)
        {
            value = record.Data.ToArray();

            return true;
        }

        value = [];

        return false;
    }

    /// <summary>
    /// Removes an attribute from a path if it exists.
    /// </summary>
    /// <param name="path">Target path.</param>
    /// <param name="attributeName">Attribute name.</param>
    /// <returns><c>true</c> if the attribute was removed; otherwise, <c>false</c>.</returns>
    public bool RemoveAttribute(string path, string attributeName)
    {
        this.disposed.ThrowIf();
        path.Required();
        ArgumentException.ThrowIfNullOrEmpty(attributeName);

        var segments = SplitPath(path);

        if (!this.TryResolveEntry(segments, out _, out var entry) || entry is null || entry.Kind != FileKind.File)
            return false;

        using var handle = this.lockManager.AcquireExclusive(entry.TargetId);
        var removed = this.attributeTable.Remove(entry.TargetId, attributeName);

        if (removed)
        {
            this.metadataPersistence.Flush();
            this.compactionEngine?.NotifyFileChanged(entry.TargetId);
        }

        return removed;
    }

    private FileId CreateDirectoryInternal(FileId parent, string name)
    {
        var directoryId = this.fileTable.Allocate(FileKind.Directory, FileAttributes.Directory);
        var now = DateTime.UtcNow;

        var entry = new DirectoryEntry
        {
            Name = name,
            TargetId = directoryId,
            Kind = FileKind.Directory,
            Attributes = FileAttributes.Directory,
            CreatedUtc = now,
            ModifiedUtc = now,
            AccessedUtc = now,
            Generation = 0,
        };

        this.directoryIndex.Upsert(parent, name, entry);

        return directoryId;
    }

    private FileId ResolveParentDirectory(string[] segments)
    {
        if (segments.Length <= 1)
            return FileId.Root;

        var parentSegments = segments[..^1];
        var current = FileId.Root;

        foreach (var segment in parentSegments)
        {
            using var handle = this.lockManager.AcquireShared(current);

            if (!this.directoryIndex.TryGet(current, segment, out var entry) || entry.Kind != FileKind.Directory)
                throw new DirectoryNotFoundException($"Directory '{segment}' not found.");

            current = entry.TargetId;
        }

        return current;
    }

    private bool TryResolveEntry(string[] segments, out FileId parentId, out DirectoryEntry? entry)
    {
        parentId = FileId.Root;
        entry = null;

        if (segments.Length == 0)
            return false;

        for (var i = 0; i < segments.Length; i++)
        {
            using var handle = this.lockManager.AcquireShared(parentId);

            if (!this.directoryIndex.TryGet(parentId, segments[i], out var found))
                return false;

            if (i == segments.Length - 1)
            {
                entry = found;

                return true;
            }

            if (found.Kind != FileKind.Directory)
                return false;

            parentId = found.TargetId;
        }

        return false;
    }

    private FileId GetDirectoryId(string[] segments)
    {
        var current = FileId.Root;

        foreach (var segment in segments)
        {
            using var handle = this.lockManager.AcquireShared(current);

            if (!this.directoryIndex.TryGet(current, segment, out var entry) || entry.Kind != FileKind.Directory)
                throw new DirectoryNotFoundException($"Directory '{string.Join('/', segments)}' not found.");

            current = entry.TargetId;
        }

        return current;
    }

    private void DeleteDirectoryContents(FileId directoryId)
    {
        var children = this.directoryIndex.Enumerate(directoryId).ToList();

        foreach (var child in children)
        {
            if (child.Kind == FileKind.Directory)
            {
                using var childLock = this.lockManager.AcquireExclusive(child.TargetId);
                this.DeleteDirectoryContents(child.TargetId);
                this.directoryIndex.Remove(directoryId, child.Name);
                this.fileTable.Remove(child.TargetId);
            }
            else if (child.Kind == FileKind.File)
                this.DeleteFileEntry(directoryId, child);
        }
    }

    private void DeleteFileEntry(FileId parentId, DirectoryEntry entry)
    {
        using var fileLock = this.lockManager.AcquireExclusive(entry.TargetId);

        if (this.fileTable.TryGet(entry.TargetId, out var metadata))
        {
            foreach (var extent in metadata.Extents)
                this.extentAllocator.Free(extent, ExtentAllocator.AllocationOwner.FileData);
        }

        this.attributeTable.RemoveAll(entry.TargetId);
        this.fileTable.Remove(entry.TargetId);
        this.directoryIndex.Remove(parentId, entry.Name);
        this.compactionEngine?.NotifyFileChanged(entry.TargetId);
    }

    private static string[] SplitPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/" || path == "\\")
            return [];

        return path.Split(pathSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private TService RequireService<TService>() where TService : class
    {
        if (this.services.TryGet(out TService? service) && service is not null)
            return service;

        throw new InvalidOperationException($"Service {typeof(TService).Name} was not registered.");
    }

    private static FileStream PrepareMirrorStream(FileStream primary)
    {
        primary.Required();

        FlushToDisk(primary);

        var primaryPath = Path.GetFullPath(primary.Name);
        var mirrorPath = GetMirrorPath(primaryPath);
        var directory = Path.GetDirectoryName(mirrorPath);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var mirror = new FileStream(mirrorPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.RandomAccess);

        try
        {
            FlushToDisk(mirror);
            SynchronizeMirrors(primary, mirror);
        }
        catch
        {
            mirror.Dispose();

            throw;
        }

        return mirror;
    }

    private static void SynchronizeMirrors(FileStream primary, FileStream mirror)
    {
        var primaryPosition = primary.Position;
        var mirrorPosition = mirror.Position;

        try
        {
            FlushToDisk(primary);
            FlushToDisk(mirror);

            SuperblockInspector.Inspect(primary, out var primaryState, out _, out var primaryValid);
            SuperblockInspector.Inspect(mirror, out var mirrorState, out _, out var mirrorValid);

            if (primaryValid && mirrorValid)
            {
                if (primaryState.Generation > mirrorState.Generation)
                    CopyStreamContents(primary, mirror);
                else if (mirrorState.Generation > primaryState.Generation)
                    CopyStreamContents(mirror, primary);
                else if (mirror.Length != primary.Length)
                {
                    mirror.SetLength(primary.Length);
                    FlushToDisk(mirror);
                }
                else if (StreamsDiffer(primary, mirror, out var preferMirror))
                {
                    if (preferMirror)
                        CopyStreamContents(mirror, primary);
                    else
                        CopyStreamContents(primary, mirror);
                }
            }
            else if (primaryValid)
                CopyStreamContents(primary, mirror);
            else if (mirrorValid)
                CopyStreamContents(mirror, primary);
            else if (mirror.Length != primary.Length)
            {
                mirror.SetLength(primary.Length);
                FlushToDisk(mirror);
            }
        }
        finally
        {
            if (primary.CanSeek)
                primary.Position = Math.Min(primaryPosition, primary.Length);

            if (mirror.CanSeek)
                mirror.Position = Math.Min(mirrorPosition, mirror.Length);
        }
    }

    private static void CopyStreamContents(FileStream source, FileStream destination)
    {
        if (ReferenceEquals(source, destination))
            throw new InvalidOperationException("Source and destination streams must differ.");

        FlushToDisk(source);

        var sourcePosition = source.Position;
        var destinationPosition = destination.Position;

        source.Position = 0;
        destination.SetLength(0);
        destination.Position = 0;

        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);

        try
        {
            int read;

            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                destination.Write(buffer, 0, read);

            FlushToDisk(destination);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);

            if (destination.CanSeek)
                destination.Position = Math.Min(destinationPosition, destination.Length);

            if (source.CanSeek)
                source.Position = Math.Min(sourcePosition, source.Length);
        }
    }

    private static string GetMirrorPath(string primaryPath) => primaryPath + ".bak";

    private static bool StreamsDiffer(FileStream primary, FileStream mirror, out bool preferMirror)
    {
        preferMirror = false;

        if (primary.Length != mirror.Length)
            return false;

        if (!TryFindMismatch(primary, mirror, out var preferSecond))
            return false;

        preferMirror = preferSecond;

        return true;
    }

    private static bool TryFindMismatch(FileStream left, FileStream right, out bool preferRight)
    {
        preferRight = false;
        var bufferSize = 1024 * 1024;
        var leftBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        var rightBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        var leftPosition = left.Position;
        var rightPosition = right.Position;

        try
        {
            left.Position = 0;
            right.Position = 0;

            while (true)
            {
                var readLeft = left.Read(leftBuffer, 0, bufferSize);
                var readRight = right.Read(rightBuffer, 0, bufferSize);

                if (readLeft != readRight)
                {
                    preferRight = readLeft < readRight;

                    return true;
                }

                if (readLeft == 0)
                    break;

                var leftSpan = leftBuffer.AsSpan(0, readLeft);
                var rightSpan = rightBuffer.AsSpan(0, readRight);

                if (!leftSpan.SequenceEqual(rightSpan))
                {
                    preferRight = ShouldPreferRight(leftSpan, rightSpan);

                    return true;
                }
            }
        }
        finally
        {
            left.Position = leftPosition;
            right.Position = rightPosition;
            ArrayPool<byte>.Shared.Return(leftBuffer);
            ArrayPool<byte>.Shared.Return(rightBuffer);
        }

        return false;
    }

    private static bool ShouldPreferRight(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        var leftSuspect = LooksLikeClearedSpan(left);
        var rightSuspect = LooksLikeClearedSpan(right);

        return leftSuspect switch
        {
            true when !rightSuspect => true,
            false when rightSuspect => false,
            _ => false,
        };
    }

    private static bool LooksLikeClearedSpan(ReadOnlySpan<byte> span)
    {
        if (span.IsEmpty)
            return true;

        var first = span[0];
        var allSame = true;

        for (var i = 1; i < span.Length; i++)
        {
            if (span[i] != first)
            {
                allSame = false;

                break;
            }
        }

        if (!allSame)
            return false;

        return first == 0 || first == 0xFF;
    }

    private static void FlushToDisk(FileStream stream)
    {
#if NET8_0_OR_GREATER
        stream.Flush(flushToDisk: true);
#else
        stream.Flush();
#endif
    }
}
