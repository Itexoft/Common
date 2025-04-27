// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.IO.VFS;

namespace Itexoft.Tests.IO.VFS;

internal static class TestContainerFactory
{
    public static ContainerScope Create(TestMode mode, ReadOnlySpan<byte> initialPrimary = default)
    {
        if (!mode.EnableMirroring)
            return new(new TestMemoryStream(initialPrimary), null);

        var primaryPath = Path.Combine(Path.GetTempPath(), $"vfs_test_{Guid.NewGuid():N}.vfs");
        var stream = CreateFileStream(primaryPath);

        if (!initialPrimary.IsEmpty)
        {
            stream.Write(initialPrimary);
            stream.Flush(true);
            stream.Position = 0;
        }

        return new(stream, primaryPath);
    }

    public static VirtualFileSystemScope Mount(
        TestMode mode,
        Func<VirtualFileSystemOptions>? configure = null,
        ReadOnlySpan<byte> initialPrimary = default)
    {
        var container = Create(mode, initialPrimary);

        try
        {
            var options = configure?.Invoke() ?? new VirtualFileSystemOptions { EnableMirroring = mode.EnableMirroring };

            if (options.EnableMirroring != mode.EnableMirroring)
                throw new InvalidOperationException("Test options must align with mirroring mode.");

            var vfs = VirtualFileSystem.Mount(container.Stream, options);

            return new(mode, container, vfs);
        }
        catch
        {
            container.Dispose();

            throw;
        }
    }

    private static FileStream CreateFileStream(string path) =>
        new(path, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite, 4096, FileOptions.RandomAccess);
}

internal sealed class ContainerScope : IDisposable
{
    internal ContainerScope(Stream stream, string? primaryPath)
    {
        this.Stream = stream;
        this.PrimaryPath = primaryPath;
    }

    public Stream Stream { get; }

    public string? PrimaryPath { get; }

    public string? MirrorPath => this.PrimaryPath is null ? null : this.PrimaryPath + ".bak";

    public void Dispose()
    {
        this.Stream.Dispose();

        if (this.PrimaryPath is null)
            return;

        TryDelete(this.PrimaryPath);
        TryDelete(this.MirrorPath);
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Ignore cleanup failures in tests.
        }
    }
}

internal sealed class VirtualFileSystemScope : IDisposable
{
    private readonly ContainerScope container;

    internal VirtualFileSystemScope(TestMode mode, ContainerScope container, VirtualFileSystem vfs)
    {
        this.Mode = mode;
        this.container = container;
        this.FileSystem = vfs;
    }

    public TestMode Mode { get; }

    public VirtualFileSystem FileSystem { get; }

    public Stream Stream => this.container.Stream;

    public string? PrimaryPath => this.container.PrimaryPath;

    public string? MirrorPath => this.container.MirrorPath;

    public void Dispose()
    {
        this.FileSystem.Dispose();
        this.container.Dispose();
    }
}
