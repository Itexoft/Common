// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Itexoft.EmbeddedWeb;

internal sealed class InMemoryArchiveFileProvider : IFileProvider
{
    private readonly EmbeddedArchiveContent _content;

    public InMemoryArchiveFileProvider(EmbeddedArchiveContent content)
        => this._content = content ?? throw new ArgumentNullException(nameof(content));

    public IDirectoryContents GetDirectoryContents(string? subpath)
    {
        var normalized = Normalize(subpath);
        var entries = new List<IFileInfo>();

        foreach (var path in this._content.Paths)
        {
            if (!IsMatch(path, normalized, out var name))
                continue;

            if (this._content.TryGetFile(path, out var file))
                entries.Add(new ArchiveFileInfo(name, file));
        }

        return entries.Count == 0
            ? NotFoundDirectoryContents.Singleton
            : new ArchiveDirectoryContents(entries);
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        var normalized = Normalize(subpath);
        if (this._content.TryGetFile(normalized, out var file))
        {
            var name = normalized.Split('/').Last();

            return new ArchiveFileInfo(name, file);
        }

        return new NotFoundFileInfo(subpath ?? string.Empty);
    }

    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;

    private static string Normalize(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        path = path.Replace('\\', '/');
        path = path.Trim('/');

        return path;
    }

    private static bool IsMatch(string fullPath, string prefix, out string name)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            var firstSeparator = fullPath.IndexOf('/');
            name = firstSeparator < 0 ? fullPath : fullPath.Substring(0, firstSeparator);

            return firstSeparator < 0;
        }

        var comparison = StringComparison.OrdinalIgnoreCase;
        if (!fullPath.StartsWith(prefix, comparison))
        {
            name = string.Empty;

            return false;
        }

        if (fullPath.Length == prefix.Length)
        {
            name = fullPath.Split('/').Last();

            return true;
        }

        if (fullPath[prefix.Length] != '/')
        {
            name = string.Empty;

            return false;
        }

        var remainder = fullPath.Substring(prefix.Length + 1);
        var childSeparator = remainder.IndexOf('/');
        if (childSeparator >= 0)
        {
            name = remainder.Substring(0, childSeparator);

            return false;
        }

        name = remainder;

        return true;
    }

    private sealed class ArchiveDirectoryContents : IDirectoryContents
    {
        private readonly IReadOnlyList<IFileInfo> _entries;

        public ArchiveDirectoryContents(IReadOnlyList<IFileInfo> entries)
            => this._entries = entries;

        public bool Exists => true;

        public IEnumerator<IFileInfo> GetEnumerator() => this._entries.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => this._entries.GetEnumerator();
    }

    private sealed class ArchiveFileInfo : IFileInfo
    {
        private readonly EmbeddedStaticFile _file;

        public ArchiveFileInfo(string name, EmbeddedStaticFile file)
        {
            this.Name = name;
            this._file = file;
        }

        public bool Exists => true;
        public long Length => this._file.Length;
        public string PhysicalPath => string.Empty;
        public string Name { get; }
        public DateTimeOffset LastModified => this._file.LastModified;
        public bool IsDirectory => false;
        public Stream CreateReadStream() => this._file.CreateReadStream();
    }
}