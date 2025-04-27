// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Concurrent;
using System.Text;
using Itexoft.IO.VFS;

namespace Itexoft.Tests.IO.VFS;

[TestFixture]
internal sealed class VirtualFileSystemTests
{
    private static int ScaleWorkload(int baseValue, int pageSize, int minimum = 1)
    {
        if (pageSize >= 4096)
            return Math.Max(minimum, baseValue);

        var scale = Math.Max(1, 4096 / Math.Max(pageSize, 1));
        var scaled = (int)Math.Ceiling(baseValue / (double)scale);

        return Math.Max(minimum, scaled);
    }

    private static int ScalePayload(int baseLength, int pageSize, int minimum = 16)
    {
        if (pageSize >= 4096)
            return Math.Max(minimum, baseLength);

        var scale = Math.Max(1, 4096 / Math.Max(pageSize, 1));
        var scaled = (int)Math.Ceiling(baseLength / (double)scale);
        var lowerBound = Math.Max(minimum, Math.Max(16, pageSize));

        return Math.Clamp(scaled, lowerBound, baseLength);
    }

    private static ParallelOptions CreateParallelOptions(int baseDegree, int pageSize)
    {
        var scaled = ScaleWorkload(baseDegree, pageSize, 1);

        return new() { MaxDegreeOfParallelism = Math.Max(1, Math.Min(baseDegree, scaled)) };
    }

    [Test]
    public void CreateDirectory_ShouldAllowNestedPaths()
    {
        using var memory = new TestMemoryStream();
        using var vfs = VirtualFileSystem.Mount(memory);

        vfs.CreateDirectory("projects/alpha");
        var dirId = vfs.ResolveDebugFileId("projects/alpha");

        Assert.That(dirId.IsValid, Is.True);
        Assert.That(vfs.DirectoryExists("projects"), Is.True);
        Assert.That(vfs.DirectoryExists("projects/alpha"), Is.True);
    }

    [Test]
    public void CreateFile_ShouldRegisterEntry()
    {
        using var memory = new TestMemoryStream();
        using var vfs = VirtualFileSystem.Mount(memory);

        vfs.CreateDirectory("data");
        vfs.CreateFile("data/config.json");
        var fileId = vfs.ResolveDebugFileId("data/config.json");

        Assert.That(fileId.IsValid, Is.True);
        Assert.That(vfs.FileExists("data/config.json"), Is.True);
    }

    [Test]
    public void CreateFile_SameName_ShouldThrow()
    {
        using var memory = new TestMemoryStream();
        using var vfs = VirtualFileSystem.Mount(memory);

        vfs.CreateDirectory("data");
        vfs.CreateFile("data/log.txt");

        Assert.Throws<IOException>(() => vfs.CreateFile("data/log.txt"));
    }

    [Test]
    public void OpenFile_ReadWrite_ShouldPersistData()
    {
        using var memory = new TestMemoryStream();
        using var vfs = VirtualFileSystem.Mount(memory);

        vfs.CreateDirectory("docs");

        using (var stream = vfs.OpenFile("docs/readme.txt", FileMode.OpenOrCreate, FileAccess.ReadWrite))
        {
            var payload = Encoding.UTF8.GetBytes("hello world");
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        using (var stream = vfs.OpenFile("docs/readme.txt", FileMode.Open, FileAccess.Read))
        {
            var buffer = new byte[32];
            var read = stream.Read(buffer, 0, buffer.Length);
            var text = Encoding.UTF8.GetString(buffer, 0, read);
            Assert.That(text, Is.EqualTo("hello world"));
        }
    }

    [Test]
    public void Attributes_SetGetRemove_ShouldWork()
    {
        using var memory = new TestMemoryStream();
        using var vfs = VirtualFileSystem.Mount(memory);

        vfs.CreateFile("app.config");
        vfs.SetAttribute("app.config", "checksum", [1, 2, 3]);

        Assert.That(vfs.TryGetAttribute("app.config", "checksum", out var value), Is.True);
        Assert.That(value, Is.EqualTo(new byte[] { 1, 2, 3 }));

        Assert.That(vfs.RemoveAttribute("app.config", "checksum"), Is.True);
        Assert.That(vfs.TryGetAttribute("app.config", "checksum", out _), Is.False);
    }

    [Test]
    public void DeleteDirectory_Recursive_ShouldRemoveTree()
    {
        using var memory = new TestMemoryStream();
        using var vfs = VirtualFileSystem.Mount(memory);

        vfs.CreateDirectory("root/child");
        vfs.CreateFile("root/child/file.dat");

        vfs.DeleteDirectory("root", true);

        Assert.That(vfs.DirectoryExists("root"), Is.False);
    }

    [Test]
    public void Compaction_ShouldMergeExtents()
    {
        using var memory = new TestMemoryStream();
        using var vfs = VirtualFileSystem.Mount(memory);

        vfs.CreateFile("data.bin");
        var fileId = vfs.ResolveDebugFileId("data.bin");

        using (var stream = vfs.OpenFile("data.bin", FileMode.Open, FileAccess.Write))
        {
            stream.SetLength(vfs.PageSize * 3);
            stream.SetLength(vfs.PageSize);
            stream.SetLength(vfs.PageSize * 3);
        }

        var metadataBefore = vfs.GetFileMetadata(fileId);
        Assert.That(metadataBefore.Extents.Length, Is.GreaterThanOrEqualTo(2));

        vfs.RunCompaction();

        var metadataAfter = vfs.GetFileMetadata(fileId);
        Assert.That(metadataAfter.Extents.Length, Is.EqualTo(1));
    }

    [Test]
    public void PowerFailure_ShouldFallbackToPreviousSuperblock()
    {
        using var memory = new TestMemoryStream();
        byte activeSlot;
        int pageSize;

        using (var vfs = VirtualFileSystem.Mount(memory))
        {
            vfs.CreateDirectory("docs");
            activeSlot = vfs.ActiveSuperblockSlot;
            pageSize = vfs.PageSize;
        }

        if (memory.TryGetBuffer(out var segment) && segment.Array is not null)
            Array.Clear(segment.Array, segment.Offset + activeSlot * pageSize, pageSize / 2);
        else
        {
            memory.Position = activeSlot * pageSize;
            memory.Write(new byte[pageSize / 2]);
        }

        memory.Position = 0;
        using var vfs2 = VirtualFileSystem.Mount(memory);
        Assert.DoesNotThrow(() => vfs2.CreateDirectory("scratch"));
    }

    [Test]
    public void CorruptedMetadata_ShouldStillMount()
    {
        using var memory = new TestMemoryStream();
        int pageSize;

        using (var vfs = VirtualFileSystem.Mount(memory))
        {
            vfs.CreateDirectory("alpha");
            vfs.CreateFile("alpha/file.txt");
            pageSize = vfs.PageSize;
        }

        if (memory.TryGetBuffer(out var segment) && segment.Array is not null)
            Array.Clear(segment.Array, segment.Offset + pageSize * 2, pageSize);
        else
        {
            memory.Position = pageSize * 2;
            memory.Write(new byte[pageSize]);
        }

        memory.Position = 0;
        using var vfs2 = VirtualFileSystem.Mount(memory);
        Assert.DoesNotThrow(() => vfs2.CreateDirectory("beta"));
    }

    [Test]
    public void NestedSingleWrite_ShouldRoundtrip()
    {
        using var root = new TestMemoryStream();
        using var outer = VirtualFileSystem.Mount(root, new() { EnableCompaction = false });
        outer.CreateDirectory("layers");

        using var container = outer.OpenFile("layers/inner.vfs", FileMode.OpenOrCreate, FileAccess.ReadWrite);
        container.SetLength(0);

        using var inner = VirtualFileSystem.Mount(container, new() { EnableCompaction = false });
        inner.CreateDirectory("data");

        var payloadA = Enumerable.Repeat((byte)1, 2048).ToArray();
        var payloadB = Enumerable.Repeat((byte)2, 2048).ToArray();

        using (var stream = inner.OpenFile("data/a.bin", FileMode.OpenOrCreate, FileAccess.ReadWrite))
        {
            stream.SetLength(0);
            stream.Write(payloadA, 0, payloadA.Length);
            stream.Flush();
        }

        using (var stream = inner.OpenFile("data/b.bin", FileMode.OpenOrCreate, FileAccess.ReadWrite))
        {
            stream.SetLength(0);
            stream.Write(payloadB, 0, payloadB.Length);
            stream.Flush();
        }

        using (var stream = inner.OpenFile("data/a.bin", FileMode.Open, FileAccess.Read))
        {
            var buffer = new byte[payloadA.Length];
            var read = stream.Read(buffer, 0, buffer.Length);
            Assert.That(read, Is.EqualTo(buffer.Length));
            Assert.That(buffer, Is.EqualTo(payloadA));
        }

        using (var stream = inner.OpenFile("data/b.bin", FileMode.Open, FileAccess.Read))
        {
            var buffer = new byte[payloadB.Length];
            var read = stream.Read(buffer, 0, buffer.Length);
            Assert.That(read, Is.EqualTo(buffer.Length));
            Assert.That(buffer, Is.EqualTo(payloadB));
        }
    }

    [Test]
    public void NestedSequentialWrites_ShouldRoundtrip()
    {
        using var root = new TestMemoryStream();
        using var outer = VirtualFileSystem.Mount(root, new() { EnableCompaction = false });
        outer.CreateDirectory("layers");

        using var container = outer.OpenFile("layers/inner.vfs", FileMode.OpenOrCreate, FileAccess.ReadWrite);
        container.SetLength(0);

        using var inner = VirtualFileSystem.Mount(container, new() { EnableCompaction = false });
        inner.CreateDirectory("data");

        var payloads = Enumerable.Range(0, 4).Select(i => Enumerable.Repeat((byte)(i + 1), 2048).ToArray()).ToArray();

        for (var i = 0; i < payloads.Length; i++)
        {
            var path = $"data/file_{i}.bin";
            using var stream = inner.OpenFile(path, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            stream.SetLength(0);
            stream.Write(payloads[i], 0, payloads[i].Length);
            stream.Flush();
        }

        for (var i = 0; i < payloads.Length; i++)
        {
            var path = $"data/file_{i}.bin";
            using var stream = inner.OpenFile(path, FileMode.Open, FileAccess.Read);
            var buffer = new byte[payloads[i].Length];
            var read = stream.Read(buffer, 0, buffer.Length);
            Assert.That(read, Is.EqualTo(buffer.Length), $"Unexpected length for {path}");
            Assert.That(buffer, Is.EqualTo(payloads[i]), $"Payload mismatch for {path}");
        }
    }

    [Test]
    public void NestedParallelWrites_ShouldDetectMismatch()
    {
        using var root = new TestMemoryStream();
        using var outer = VirtualFileSystem.Mount(root, new() { EnableCompaction = false });
        outer.CreateDirectory("layers");

        using var container = outer.OpenFile("layers/inner.vfs", FileMode.OpenOrCreate, FileAccess.ReadWrite);
        container.SetLength(0);

        using var inner = VirtualFileSystem.Mount(container, new() { EnableCompaction = false });
        inner.CreateDirectory("data");

        var baseWorkers = 4;
        var workerCount = Math.Max(1, ScaleWorkload(baseWorkers, inner.PageSize));
        var payloadLength = ScalePayload(2048, inner.PageSize);
        var payloads = Enumerable.Range(0, workerCount).Select(i => Enumerable.Repeat((byte)(i + 1), payloadLength).ToArray()).ToArray();

        for (var index = 0; index < payloads.Length; index++)
        {
            var path = $"data/file_{index}.bin";
            using var stream = inner.OpenFile(path, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            stream.SetLength(0);
            stream.Write(payloads[index], 0, payloads[index].Length);
            stream.Flush();
        }

        var iterations = Math.Max(1, ScaleWorkload(250, inner.PageSize));
        var parallelOptions = CreateParallelOptions(baseWorkers, inner.PageSize);

        Parallel.For(
            0,
            workerCount,
            parallelOptions,
            worker =>
            {
                var path = $"data/file_{worker}.bin";
                var buffer = payloads[worker];
                using var stream = inner.OpenFile(path, FileMode.OpenOrCreate, FileAccess.ReadWrite);

                for (var iteration = 0; iteration < iterations; iteration++)
                {
                    stream.Position = 0;
                    stream.SetLength(0);
                    stream.Write(buffer, 0, buffer.Length);
                    stream.Flush();
                }
            });

        var mismatches = new List<int>();

        for (var i = 0; i < payloads.Length; i++)
        {
            var path = $"data/file_{i}.bin";
            using var stream = inner.OpenFile(path, FileMode.Open, FileAccess.Read);
            var buffer = new byte[payloads[i].Length];
            var read = stream.Read(buffer, 0, buffer.Length);

            if (read != buffer.Length || !buffer.AsSpan().SequenceEqual(payloads[i]))
                mismatches.Add(i);
        }

        Assert.That(mismatches, Is.Empty, $"Mismatched files: {string.Join(", ", mismatches)}");
    }

    [Test]
    public void SequentialTruncateRewrite_ShouldRemainIsolated()
    {
        using var memory = new TestMemoryStream();
        using var vfs = VirtualFileSystem.Mount(memory, new() { EnableCompaction = false });
        vfs.CreateDirectory("data");

        var payloadA = Enumerable.Repeat((byte)0x11, 2048).ToArray();
        var payloadB = Enumerable.Repeat((byte)0x22, 2048).ToArray();

        vfs.CreateFile("data/a.bin");
        vfs.CreateFile("data/b.bin");

        using (var streamA = vfs.OpenFile("data/a.bin", FileMode.Open, FileAccess.ReadWrite))
        using (var streamB = vfs.OpenFile("data/b.bin", FileMode.Open, FileAccess.ReadWrite))
        {
            for (var iteration = 0; iteration < 200; iteration++)
            {
                streamA.Position = 0;
                streamA.SetLength(0);
                streamA.Write(payloadA, 0, payloadA.Length);
                streamA.Flush();

                streamB.Position = 0;
                streamB.SetLength(0);
                streamB.Write(payloadB, 0, payloadB.Length);
                streamB.Flush();

                streamA.Position = 0;
                var bufferA = new byte[payloadA.Length];
                var readA = streamA.Read(bufferA, 0, bufferA.Length);
                Assert.That(readA, Is.EqualTo(bufferA.Length));
                Assert.That(bufferA, Is.EqualTo(payloadA), $"Iteration {iteration}: file A corrupted");

                streamB.Position = 0;
                var bufferB = new byte[payloadB.Length];
                var readB = streamB.Read(bufferB, 0, bufferB.Length);
                Assert.That(readB, Is.EqualTo(bufferB.Length));
                Assert.That(bufferB, Is.EqualTo(payloadB), $"Iteration {iteration}: file B corrupted");
            }
        }
    }

    [Test]
    public void ParallelTruncateRewrite_ShouldRemainIsolated()
    {
        using var memory = new TestMemoryStream();
        using var vfs = VirtualFileSystem.Mount(memory, new() { EnableCompaction = false });
        vfs.CreateDirectory("data");

        var payloadA = Enumerable.Repeat((byte)0x31, 2048).ToArray();
        var payloadB = Enumerable.Repeat((byte)0x42, 2048).ToArray();

        vfs.CreateFile("data/a.bin");
        vfs.CreateFile("data/b.bin");

        var iterations = 500;

        Parallel.Invoke(
            () =>
            {
                using var stream = vfs.OpenFile("data/a.bin", FileMode.Open, FileAccess.ReadWrite);
                var buffer = new byte[payloadA.Length];

                for (var iteration = 0; iteration < iterations; iteration++)
                {
                    stream.Position = 0;
                    stream.SetLength(0);
                    stream.Write(payloadA, 0, payloadA.Length);
                    stream.Flush();

                    stream.Position = 0;
                    var read = stream.Read(buffer, 0, buffer.Length);

                    if (read != buffer.Length || !buffer.AsSpan().SequenceEqual(payloadA))
                        Assert.Fail($"Parallel iteration {iteration}: file A corrupted (value {buffer[0]:X2})");
                }
            },
            () =>
            {
                using var stream = vfs.OpenFile("data/b.bin", FileMode.Open, FileAccess.ReadWrite);
                var buffer = new byte[payloadB.Length];

                for (var iteration = 0; iteration < iterations; iteration++)
                {
                    stream.Position = 0;
                    stream.SetLength(0);
                    stream.Write(payloadB, 0, payloadB.Length);
                    stream.Flush();

                    stream.Position = 0;
                    var read = stream.Read(buffer, 0, buffer.Length);

                    if (read != buffer.Length || !buffer.AsSpan().SequenceEqual(payloadB))
                        Assert.Fail($"Parallel iteration {iteration}: file B corrupted (value {buffer[0]:X2})");
                }
            });
    }

    [Test]
    public void ParallelCreateDelete_ShouldLeaveAllocatorClean()
    {
        using var memory = new TestMemoryStream();
        using var vfs = VirtualFileSystem.Mount(memory, new() { EnableCompaction = false });
        vfs.CreateDirectory("temp");

        var baseWorkers = Math.Min(Environment.ProcessorCount, 8);
        var workerCount = Math.Max(1, ScaleWorkload(baseWorkers, vfs.PageSize));
        var iterations = Math.Max(1, ScaleWorkload(20, vfs.PageSize));
        var parallelOptions = CreateParallelOptions(baseWorkers, vfs.PageSize);

        Parallel.For(
            0,
            workerCount,
            parallelOptions,
            worker =>
            {
                var random = new Random(worker * 7919);

                for (var iteration = 0; iteration < iterations; iteration++)
                {
                    var path = $"temp/file_{worker}_{iteration}.bin";
                    vfs.CreateFile(path);
                    var length = 4096 + random.Next(0, 4) * 4096;
                    var buffer = new byte[length];
                    random.NextBytes(buffer);

                    using (var stream = vfs.OpenFile(path, FileMode.Open, FileAccess.ReadWrite))
                    {
                        stream.Write(buffer, 0, buffer.Length);
                        stream.Flush();

                        stream.Position = 0;
                        var readBuffer = new byte[buffer.Length];
                        var read = stream.Read(readBuffer, 0, readBuffer.Length);
                        Assert.That(read, Is.EqualTo(buffer.Length));
                        Assert.That(readBuffer, Is.EqualTo(buffer));
                    }

                    Assert.That(vfs.FileExists(path), Is.True);
                    vfs.DeleteFile(path);
                    Assert.That(vfs.FileExists(path), Is.False);
                }
            });

        var owners = vfs.CaptureDebugPageOwners();
        Assert.That(owners, Is.Empty);
    }

    [Test]
    public void NestedParallelSetLength_ShouldMatchLastWriter()
    {
        using var root = new TestMemoryStream();
        var fileSystems = new List<VirtualFileSystem>();
        var hostStreams = new List<Stream>();

        Stream current = root;

        for (var layer = 0; layer < 4; layer++)
        {
            var vfs = VirtualFileSystem.Mount(current, new() { EnableCompaction = false });
            fileSystems.Add(vfs);

            if (layer < 3)
            {
                vfs.CreateDirectory("layers");
                var path = $"layers/layer_{layer}.vfs";
                var stream = vfs.OpenFile(path, FileMode.OpenOrCreate, FileAccess.ReadWrite);
                stream.SetLength(0);
                hostStreams.Add(stream);
                current = stream;
            }
        }

        var innermost = fileSystems[^1];
        innermost.CreateDirectory("data");
        var baseFileCount = Math.Min(Environment.ProcessorCount, 8);
        var fileCount = Math.Max(1, ScaleWorkload(baseFileCount, innermost.PageSize));
        var payloadLength = ScalePayload(2048, innermost.PageSize);
        var payloads = Enumerable.Range(0, fileCount).Select(i => Enumerable.Repeat((byte)(i + 1), payloadLength).ToArray()).ToArray();

        var filePaths = new string[fileCount];

        for (var i = 0; i < fileCount; i++)
        {
            var path = $"data/file_{i}.bin";
            filePaths[i] = path;
            innermost.CreateFile(path);
        }

        var sequence = 0;
        var lastWrites = new ConcurrentDictionary<int, (int Sequence, byte Value)>();

        var iterations = Math.Max(1, ScaleWorkload(200, innermost.PageSize));
        var parallelOptions = CreateParallelOptions(baseFileCount, innermost.PageSize);

        Parallel.For(
            0,
            fileCount,
            parallelOptions,
            fileIndex =>
            {
                var payload = payloads[fileIndex];
                var path = filePaths[fileIndex];

                for (var iteration = 0; iteration < iterations; iteration++)
                {
                    using var stream = innermost.OpenFile(path, FileMode.Open, FileAccess.ReadWrite);
                    stream.Position = 0;
                    stream.SetLength(0);
                    stream.Write(payload, 0, payload.Length);
                    stream.Flush();

                    var order = Interlocked.Increment(ref sequence);

                    lastWrites.AddOrUpdate(
                        fileIndex,
                        (order, payload[0]),
                        (_, existing) => existing.Sequence > order ? existing : (order, payload[0]));
                }
            });

        for (var i = 0; i < fileCount; i++)
        {
            var path = filePaths[i];
            using var stream = innermost.OpenFile(path, FileMode.Open, FileAccess.Read);
            var buffer = new byte[payloads[i].Length];
            var read = stream.Read(buffer, 0, buffer.Length);
            Assert.That(read, Is.EqualTo(buffer.Length));
            var expected = lastWrites[i].Value;

            foreach (var b in buffer)
                Assert.That(b, Is.EqualTo(expected), $"File {path} inconsistent: expected {expected:X2} actual {b:X2}");
        }

        for (var i = fileSystems.Count - 1; i >= 0; i--)
            fileSystems[i].Dispose();

        for (var i = hostStreams.Count - 1; i >= 0; i--)
            hostStreams[i].Dispose();
    }
}
