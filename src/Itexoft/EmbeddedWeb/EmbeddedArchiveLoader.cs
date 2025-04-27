// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace Itexoft.EmbeddedWeb;

internal static class EmbeddedArchiveLoader
{
    public static async Task<EmbeddedArchiveContent> LoadAsync(IEmbeddedArchiveSource source, CancellationToken cancellationToken)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        await using var stream = await source.OpenArchiveAsync(cancellationToken).ConfigureAwait(false);

        if (!stream.CanSeek)
            throw new InvalidOperationException("Archive stream must be seekable.");

        var header = ArrayPool<byte>.Shared.Rent(4);
        try
        {
            var read = await stream.ReadAsync(header.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
            stream.Seek(0, SeekOrigin.Begin);

            if (read >= 2 && header[0] == 0x50 && header[1] == 0x4B) // PK
                return await LoadZipAsync(stream, cancellationToken).ConfigureAwait(false);

            if (read >= 2 && header[0] == 0x1F && header[1] == 0x8B) // gzip
                return await LoadTarGzAsync(stream, cancellationToken).ConfigureAwait(false);

            return await LoadTarAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(header);
        }
    }

    private static async Task<EmbeddedArchiveContent> LoadZipAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, false, Encoding.UTF8);
        var files = new Dictionary<string, EmbeddedStaticFile>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                continue;

            using var entryStream = entry.Open();
            var content = await ReadAllBytesAsync(entryStream, cancellationToken).ConfigureAwait(false);
            var lastWriteTime = entry.LastWriteTime == DateTimeOffset.MinValue
                ? DateTimeOffset.UtcNow
                : entry.LastWriteTime;
            files[NormalizePath(entry.FullName)] = new(content, lastWriteTime);
        }

        return new(files);
    }

    private static async Task<EmbeddedArchiveContent> LoadTarGzAsync(Stream stream, CancellationToken cancellationToken)
    {
        await using var gzip = new GZipStream(stream, CompressionMode.Decompress, false);
        await using var buffer = new MemoryStream();
        await gzip.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Seek(0, SeekOrigin.Begin);

        return await LoadTarAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    private static Task<EmbeddedArchiveContent> LoadTarAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new TarReader(stream, false);
        var files = new Dictionary<string, EmbeddedStaticFile>(StringComparer.OrdinalIgnoreCase);

        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.EntryType is TarEntryType.Directory or TarEntryType.GlobalExtendedAttributes or TarEntryType.ExtendedAttributes)
                continue;

            using var target = new MemoryStream(capacity: entry.DataStream?.Length > 0 ? (int)entry.DataStream.Length : 0);
            if (entry.DataStream is { } dataStream)
                dataStream.CopyTo(target);
            var content = target.ToArray();
            var lastWriteTime = entry.ModificationTime == DateTimeOffset.MinValue
                ? DateTimeOffset.UtcNow
                : entry.ModificationTime;
            files[NormalizePath(entry.Name)] = new(content, lastWriteTime);
        }

        return Task.FromResult(new EmbeddedArchiveContent(files));
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream is MemoryStream ms && ms.TryGetBuffer(out var segment))
            return segment.ToArray();

        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        return buffer.ToArray();
    }

    private static string NormalizePath(string path)
    {
        path = path.Replace('\\', '/');
        path = path.Trim('/');

        // Tar archives may contain ./ prefix
        if (path.StartsWith("./", StringComparison.Ordinal))
            path = path[2..];

        return path;
    }
}

internal sealed class EmbeddedArchiveContent
{
    private readonly Dictionary<string, EmbeddedStaticFile> _files;

    public EmbeddedArchiveContent(Dictionary<string, EmbeddedStaticFile> files) =>
        this._files = files ?? throw new ArgumentNullException(nameof(files));

    public IReadOnlyCollection<string> Paths => this._files.Keys;

    public bool TryGetFile(string path, [MaybeNullWhen(false)] out EmbeddedStaticFile file)
        => this._files.TryGetValue(NormalizeRequestPath(path), out file);

    private static string NormalizeRequestPath(string path)
    {
        path = path.Replace('\\', '/');
        path = path.Trim('/');

        return path;
    }
}

internal sealed class EmbeddedStaticFile
{
    public EmbeddedStaticFile(byte[] content, DateTimeOffset lastModified)
    {
        this.Content = content ?? throw new ArgumentNullException(nameof(content));
        this.LastModified = lastModified;
    }

    public byte[] Content { get; }
    public DateTimeOffset LastModified { get; }
    public long Length => this.Content.Length;

    public Stream CreateReadStream() => new MemoryStream(this.Content, false);
}