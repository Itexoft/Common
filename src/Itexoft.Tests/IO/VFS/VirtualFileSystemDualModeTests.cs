// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.IO.VFS.Core;

namespace Itexoft.IO.VFS.Tests;

[TestFixtureSource(typeof(TestModes), nameof(TestModes.All))]
internal sealed class VirtualFileSystemDualModeTests : VirtualFileSystemTestBase
{
    public VirtualFileSystemDualModeTests(TestMode mode) : base(mode) { }

    [Test]
    public void FileRoundtrip_ShouldMatchPayload()
    {
        using var scope = this.MountFileSystem(() => new()
        {
            EnableMirroring = this.Mode.EnableMirroring,
            EnableCompaction = false
        });
        var vfs = scope.FileSystem;

        vfs.CreateDirectory("data");
        var payload = Enumerable.Range(0, 1024).Select(i => (byte)(i % 251)).ToArray();

        using (var stream = vfs.OpenFile("data/payload.bin", FileMode.OpenOrCreate, FileAccess.ReadWrite))
        {
            stream.SetLength(0);
            this.WriteAll(stream, payload);
            stream.Flush();
        }

        using (var stream = vfs.OpenFile("data/payload.bin", FileMode.Open, FileAccess.Read))
        {
            var buffer = new byte[payload.Length];
            var read = stream.Read(buffer, 0, buffer.Length);
            Assert.That(read, Is.EqualTo(buffer.Length));
            Assert.That(buffer, Is.EqualTo(payload));
        }
    }

    [Test]
    public void Parallel_WritesWithChunkConfiguration_ShouldRemainConsistent()
    {
        using var scope = this.MountFileSystem(() => new()
        {
            EnableMirroring = this.Mode.EnableMirroring,
            EnableCompaction = false
        });
        var vfs = scope.FileSystem;
        vfs.CreateDirectory("data");

        var payloads = Enumerable.Range(0, 4)
            .Select(i => Enumerable.Repeat((byte)(i + 1), 129).ToArray())
            .ToArray();

        Parallel.For(
            0,
            payloads.Length,
            worker =>
            {
                var path = $"data/file_{worker}.bin";
                using var stream = vfs.OpenFile(path, FileMode.OpenOrCreate, FileAccess.ReadWrite);
                for (var iteration = 0; iteration < 24; iteration++)
                {
                    stream.Position = 0;
                    stream.SetLength(0);
                    this.WriteAll(stream, payloads[worker]);
                    stream.Flush();
                }
            });

        for (var i = 0; i < payloads.Length; i++)
        {
            var path = $"data/file_{i}.bin";
            using var stream = vfs.OpenFile(path, FileMode.Open, FileAccess.Read);
            var buffer = new byte[payloads[i].Length];
            var read = stream.Read(buffer, 0, buffer.Length);
            Assert.That(read, Is.EqualTo(buffer.Length));
            Assert.That(buffer, Is.EqualTo(payloads[i]));
        }
    }

    [Test]
    public void Mirroring_PrimaryCorruption_ShouldRecoverFromBackup()
    {
        if (!this.Mode.EnableMirroring)
        {
            Assert.Pass("Primary corruption scenario is only applicable when mirroring is enabled.");

            return;
        }

        var container = TestContainerFactory.Create(this.Mode);
        try
        {
            var options = new VirtualFileSystemOptions
            {
                EnableMirroring = true,
                EnableCompaction = false
            };

            var payload = Enumerable.Repeat((byte)0x5A, 4096).ToArray();

            using (var vfs = VirtualFileSystem.Mount(container.Stream, options))
            {
                vfs.CreateFile("data.bin");
                using var stream = vfs.OpenFile("data.bin", FileMode.Open, FileAccess.Write);
                stream.SetLength(0);
                this.WriteAll(stream, payload);
                stream.Flush();
            }

            container.Stream.Position = 1024;
            container.Stream.WriteByte(0xFF);
            container.Stream.Flush();
            container.Stream.Position = 0;

            using (var restored = VirtualFileSystem.Mount(container.Stream, options))
            {
                using var readStream = restored.OpenFile("data.bin", FileMode.Open, FileAccess.Read);
                var buffer = new byte[payload.Length];
                var read = readStream.Read(buffer, 0, buffer.Length);
                Assert.That(read, Is.EqualTo(buffer.Length));
                Assert.That(buffer, Is.EqualTo(payload));
            }
        }
        finally
        {
            container.Dispose();
        }
    }

    [Test]
    public void Mirroring_BackupCorruption_ShouldRegenerateMirror()
    {
        if (!this.Mode.EnableMirroring)
        {
            Assert.Pass("Backup corruption scenario is only applicable when mirroring is enabled.");

            return;
        }

        var container = TestContainerFactory.Create(this.Mode);
        try
        {
            var options = new VirtualFileSystemOptions
            {
                EnableMirroring = true,
                EnableCompaction = false
            };

            var payload = Enumerable.Repeat((byte)0xA5, 4096).ToArray();

            using (var vfs = VirtualFileSystem.Mount(container.Stream, options))
            {
                vfs.CreateFile("data.bin");
                using var stream = vfs.OpenFile("data.bin", FileMode.Open, FileAccess.Write);
                stream.SetLength(0);
                this.WriteAll(stream, payload);
                stream.Flush();
            }

            using (var corrupt = new FileStream(container.MirrorPath!, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            {
                corrupt.Position = 2048;
                corrupt.WriteByte(0xFF);
            }

            container.Stream.Position = 0;
            using (var restored = VirtualFileSystem.Mount(container.Stream, options))
            {
                using var readStream = restored.OpenFile("data.bin", FileMode.Open, FileAccess.Read);
                var buffer = new byte[payload.Length];
                var read = readStream.Read(buffer, 0, buffer.Length);
                Assert.That(read, Is.EqualTo(buffer.Length));
                Assert.That(buffer, Is.EqualTo(payload));
            }

            var mirrorBytes = ReadAllBytesShared(container.MirrorPath!);
            var primaryBytes = ReadAllBytesShared(container.PrimaryPath!);
            Assert.That(mirrorBytes, Is.EqualTo(primaryBytes));
        }
        finally
        {
            container.Dispose();
        }
    }

    [Test]
    public void Mount_WithMismatchedPageSize_ShouldThrow()
    {
        var container = TestContainerFactory.Create(this.Mode);
        try
        {
            var initialOptions = new VirtualFileSystemOptions
            {
                EnableMirroring = this.Mode.EnableMirroring,
                EnableCompaction = false
            };

            using (var vfs = VirtualFileSystem.Mount(container.Stream, initialOptions))
            {
                vfs.CreateDirectory("seed");
            }

            container.Stream.Position = 0;

            var mismatchOptions = new VirtualFileSystemOptions
            {
                EnableMirroring = this.Mode.EnableMirroring,
                EnableCompaction = false,
                PageSize = PageSizing.DefaultPageSize / 2
            };

            Assert.Throws<InvalidOperationException>(() => VirtualFileSystem.Mount(container.Stream, mismatchOptions));
        }
        finally
        {
            container.Dispose();
        }
    }

    private static byte[] ReadAllBytesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var result = new byte[stream.Length];
        var offset = 0;
        int read;
        while ((read = stream.Read(result, offset, result.Length - offset)) > 0)
            offset += read;

        return result;
    }
}