// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using Itexoft.IO.VFS;
using Itexoft.IO.VFS.Core;
using Itexoft.IO.VFS.Metadata.Models;

namespace Itexoft.Tests.IO.VFS;

[TestFixture]
internal sealed class VirtualFileSystemStressTests
{
    private static StressProfile CreateProfile(VirtualFileSystem vfs) => new(vfs.PageSize);

    [Test]
    public void Parallel_WritesAndReads_ShouldStayConsistent()
    {
        using var memory = new TestMemoryStream();
        using var vfs = VirtualFileSystem.Mount(memory, new() { EnableCompaction = false });

        vfs.CreateDirectory("users");
        var profile = CreateProfile(vfs);
        var baseFileCount = Environment.ProcessorCount * 2;
        var fileCount = Math.Max(1, profile.Workload(baseFileCount));
        var payloadLength = profile.Payload(4096);
        var payloads = Enumerable.Range(0, fileCount).Select(i => Enumerable.Repeat((byte)i, payloadLength).ToArray()).ToArray();
        var fileIds = new FileId[fileCount];

        for (var i = 0; i < fileCount; i++)
        {
            var path = $"users/file_{i}.bin";
            vfs.CreateFile(path);
            fileIds[i] = vfs.ResolveDebugFileId(path);
            TestContext.WriteLine($"Created file index {i} => id {fileIds[i].Value}");
        }

        var parallelOptions = profile.CreateParallelOptions(baseFileCount);

        Parallel.For(
            0,
            payloads.Length,
            parallelOptions,
            i =>
            {
                var path = $"users/file_{i}.bin";
                using var stream = vfs.OpenFile(path, FileMode.Open, FileAccess.ReadWrite);
                stream.SetLength(0);
                stream.Write(payloads[i], 0, payloads[i].Length);
                stream.Flush();
            });

        var allocatedPages = new Dictionary<long, int>();

        for (var i = 0; i < fileCount; i++)
        {
            var metadata = vfs.GetFileMetadata(fileIds[i]);
            Assert.That(metadata.Length, Is.EqualTo(payloads[i].Length), $"File {i} length mismatch");
            Assert.That(metadata.Extents.Length, Is.GreaterThan(0), $"File {i} has no extents");

            foreach (var extent in metadata.Extents)
            {
                for (var page = 0; page < extent.Length; page++)
                {
                    var pageId = extent.Start.Value + page;

                    if (allocatedPages.TryGetValue(pageId, out var owner))
                        Assert.Fail($"Duplicate page {pageId} detected for files {owner} and {i}");

                    allocatedPages[pageId] = i;
                }
            }
        }

        Parallel.For(
            0,
            payloads.Length,
            parallelOptions,
            i =>
            {
                var path = $"users/file_{i}.bin";
                using var stream = vfs.OpenFile(path, FileMode.Open, FileAccess.Read);
                var buffer = new byte[payloads[i].Length];
                var read = stream.Read(buffer, 0, buffer.Length);
                Assert.That(read, Is.EqualTo(buffer.Length));

                if (!buffer.AsSpan().SequenceEqual(payloads[i]))
                {
                    var metadata = vfs.GetFileMetadata(fileIds[i]);
                    TestContext.WriteLine($"Mismatch for file {i}: expected {payloads[i][0]}, actual {buffer[0]}");

                    foreach (var extent in metadata.Extents)
                        TestContext.WriteLine($"  extent {extent.Start.Value}..{extent.EndExclusive} length {extent.Length}");

                    Assert.That(buffer, Is.EqualTo(payloads[i]));
                }
            });
    }

    [Test]
    public void BulkWrite_PerformanceSmokeTest()
    {
        using var memory = new TestMemoryStream();
        using var vfs = VirtualFileSystem.Mount(memory, new() { EnableCompaction = false });

        var profile = CreateProfile(vfs);
        var random = new Random(7);
        var baseBuffer = 1024 * 1024;
        var bufferLength = profile.Payload(baseBuffer, vfs.PageSize);
        var totalBytes = Math.Max((long)bufferLength * profile.Workload(32), vfs.PageSize * 128L);
        var thresholdMs = profile.TimeBudgetMs(5_000, 12);

        var buffer = new byte[bufferLength];
        random.NextBytes(buffer);

        var sw = Stopwatch.StartNew();

        using (var stream = vfs.OpenFile("bulk.dat", FileMode.Create, FileAccess.ReadWrite))
        {
            long written = 0;

            while (written < totalBytes)
            {
                var chunk = (int)Math.Min(bufferLength, totalBytes - written);
                stream.Write(buffer, 0, chunk);
                written += chunk;
            }

            stream.Flush();
        }

        sw.Stop();

        Assert.That(
            sw.ElapsedMilliseconds,
            Is.LessThan(thresholdMs),
            $"Bulk write took too long ({sw.ElapsedMilliseconds} ms) with page size {vfs.PageSize} and payload {bufferLength}.");
    }

    [Test]
    public void SingleFile_WriteRead_ShouldRoundtrip()
    {
        using var memory = new TestMemoryStream();
        using var vfs = VirtualFileSystem.Mount(memory, new() { EnableCompaction = false });
        vfs.CreateDirectory("users");

        const string path = "users/roundtrip.bin";
        var payload = Enumerable.Repeat((byte)42, 2048).ToArray();
        vfs.CreateFile(path);

        using (var stream = vfs.OpenFile(path, FileMode.Open, FileAccess.ReadWrite))
        {
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        using (var stream = vfs.OpenFile(path, FileMode.Open, FileAccess.Read))
        {
            var buffer = new byte[payload.Length];
            var read = stream.Read(buffer, 0, buffer.Length);
            Assert.That(read, Is.EqualTo(buffer.Length));
            Assert.That(buffer, Is.EqualTo(payload));
        }
    }

    [Test]
    public void NestedVirtualFileSystems_ShouldRemainConsistent()
    {
        const int layerCount = 4;

        using var root = new TestMemoryStream();
        var fileSystems = new List<VirtualFileSystem>(layerCount);
        var hostStreams = new List<Stream>(layerCount - 1);

        Stream currentStream = root;

        for (var layer = 0; layer < layerCount; layer++)
        {
            var vfs = VirtualFileSystem.Mount(currentStream, new() { EnableCompaction = false });
            fileSystems.Add(vfs);

            if (layer < layerCount - 1)
            {
                vfs.CreateDirectory("layers");
                var containerPath = $"layers/layer_{layer}.vfs";
                var containerStream = vfs.OpenFile(containerPath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
                containerStream.SetLength(0);
                hostStreams.Add(containerStream);
                currentStream = containerStream;
            }
        }

        var innermost = fileSystems[^1];
        innermost.CreateDirectory("data");
        var baseFileCount = Environment.ProcessorCount;
        var profile = CreateProfile(innermost);
        var fileCount = Math.Max(1, profile.Workload(baseFileCount));
        var payloadLength = profile.Payload(2048);
        var payloads = Enumerable.Range(0, fileCount).Select(i => Enumerable.Repeat((byte)(i % 250 + 1), payloadLength).ToArray()).ToArray();

        var parallelOptions = profile.CreateParallelOptions(baseFileCount);

        Parallel.For(
            0,
            payloads.Length,
            parallelOptions,
            i =>
            {
                var path = $"data/file_{i}.bin";
                using var stream = innermost.OpenFile(path, FileMode.OpenOrCreate, FileAccess.ReadWrite);
                stream.SetLength(0);
                stream.Write(payloads[i], 0, payloads[i].Length);
                stream.Flush();
            });

        Parallel.For(
            0,
            payloads.Length,
            parallelOptions,
            i =>
            {
                var path = $"data/file_{i}.bin";
                using var stream = innermost.OpenFile(path, FileMode.Open, FileAccess.Read);
                var buffer = new byte[payloads[i].Length];
                var read = stream.Read(buffer, 0, buffer.Length);
                Assert.That(read, Is.EqualTo(buffer.Length));
                Assert.That(buffer, Is.EqualTo(payloads[i]));
            });

        for (var index = fileSystems.Count - 1; index >= 0; index--)
        {
            if (index < hostStreams.Count)
                hostStreams[index].Dispose();

            fileSystems[index].Dispose();
        }

        root.Position = 0;

        var remountFileSystems = new List<VirtualFileSystem>(layerCount);
        var remountStreams = new List<Stream>(layerCount - 1);
        currentStream = root;

        for (var layer = 0; layer < layerCount; layer++)
        {
            var vfs = VirtualFileSystem.Mount(currentStream, new() { EnableCompaction = false });
            remountFileSystems.Add(vfs);

            if (layer < layerCount - 1)
            {
                var containerPath = $"layers/layer_{layer}.vfs";
                var stream = vfs.OpenFile(containerPath, FileMode.Open, FileAccess.ReadWrite);
                remountStreams.Add(stream);
                currentStream = stream;
            }
        }

        var remountedInnermost = remountFileSystems[^1];

        using (var reopened = remountedInnermost.OpenFile("data/file_0.bin", FileMode.Open, FileAccess.Read))
        {
            var buffer = new byte[payloads[0].Length];
            var read = reopened.Read(buffer, 0, buffer.Length);
            Assert.That(read, Is.EqualTo(buffer.Length));
            Assert.That(buffer, Is.EqualTo(payloads[0]));
        }

        for (var index = remountFileSystems.Count - 1; index >= 0; index--)
        {
            remountFileSystems[index].Dispose();

            if (index < remountStreams.Count)
                remountStreams[index].Dispose();
        }
    }

    [Test]
    public void NestedVirtualFileSystems_Sequential_Debug()
    {
        using var root = new TestMemoryStream();
        using var outer = VirtualFileSystem.Mount(root, new() { EnableCompaction = false });
        outer.CreateDirectory("layers");

        using var container = outer.OpenFile("layers/inner.vfs", FileMode.OpenOrCreate, FileAccess.ReadWrite);
        container.SetLength(0);

        using var inner = VirtualFileSystem.Mount(container, new() { EnableCompaction = false });
        inner.CreateDirectory("data");

        byte[][] payloads =
        [
            Enumerable.Repeat((byte)1, 2048).ToArray(), Enumerable.Repeat((byte)2, 2048).ToArray(), Enumerable.Repeat((byte)3, 2048).ToArray(),
        ];

        for (var index = 0; index < payloads.Length; index++)
        {
            var path = $"data/file_{index}.bin";
            using var stream = inner.OpenFile(path, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            stream.SetLength(0);
            stream.Write(payloads[index], 0, payloads[index].Length);
            stream.Flush();
        }

        string.Join(" | ", inner.EnumerateDebugFileMetadata());
        inner.DescribeDebugUsage();
        string.Join("; ", inner.FindDebugDuplicatePages().Select(d => $"page={d.Page} owners={string.Join(',', d.Files)}"));

        for (var index = 0; index < payloads.Length; index++)
        {
            var path = $"data/file_{index}.bin";
            using var stream = inner.OpenFile(path, FileMode.Open, FileAccess.Read);
            var buffer = new byte[payloads[index].Length];
            var read = stream.Read(buffer, 0, buffer.Length);
            Assert.That(read, Is.EqualTo(buffer.Length), $"Sequential nested read truncated for {path}");
            Assert.That(buffer, Is.EqualTo(payloads[index]), $"Sequential nested mismatch for {path}");
        }
    }

    [Test]
    public void NestedVirtualFileSystems_TwoFiles_Parallel_Debug()
    {
        using var root = new TestMemoryStream();
        var fileSystems = new List<VirtualFileSystem>(2);

        Stream current = root;

        for (var layer = 0; layer < 2; layer++)
        {
            var vfs = VirtualFileSystem.Mount(current, new() { EnableCompaction = false });
            fileSystems.Add(vfs);

            if (layer == 0)
            {
                vfs.CreateDirectory("layers");
                var host = vfs.OpenFile("layers/inner.vfs", FileMode.OpenOrCreate, FileAccess.ReadWrite);
                host.SetLength(0);
                current = host;
            }
        }

        var inner = fileSystems[^1];
        inner.CreateDirectory("data");
        var profile = CreateProfile(inner);
        var payloadLength = profile.Payload(4096);

        byte[][] payloads = [Enumerable.Repeat((byte)0x11, payloadLength).ToArray(), Enumerable.Repeat((byte)0x22, payloadLength).ToArray()];

        var pageSamples = new List<string>();

        var parallelOptions = profile.CreateParallelOptions(payloads.Length);

        Parallel.For(
            0,
            payloads.Length,
            parallelOptions,
            index =>
            {
                var path = $"data/file_{index}.bin";
                using var stream = inner.OpenFile(path, FileMode.OpenOrCreate, FileAccess.ReadWrite);
                stream.SetLength(0);
                stream.Write(payloads[index], 0, payloads[index].Length);
                stream.Flush();
            });

        var mismatches = new List<string>();

        for (var index = 0; index < payloads.Length; index++)
        {
            var path = $"data/file_{index}.bin";
            using var stream = inner.OpenFile(path, FileMode.Open, FileAccess.Read);
            var buffer = new byte[payloads[index].Length];
            var read = stream.Read(buffer, 0, buffer.Length);

            if (read != buffer.Length)
                mismatches.Add($"{path}: truncated read {read} of {buffer.Length}");
            else if (!buffer.AsSpan().SequenceEqual(payloads[index]))
                mismatches.Add($"{path}: first=0x{buffer[0]:X2}");
        }

        // Capture allocator state after operations.
        var metadataDump = string.Join(" | ", inner.EnumerateDebugFileMetadata());
        var usage = inner.DescribeDebugUsage();
        var duplicatesDump = string.Join("; ", inner.FindDebugDuplicatePages().Select(d => $"page={d.Page} owners={string.Join(',', d.Files)}"));

        for (var index = 0; index < payloads.Length; index++)
        {
            var path = $"data/file_{index}.bin";
            var fileId = inner.ResolveDebugFileId(path);

            if (!fileId.IsValid)
            {
                pageSamples.Add($"{path} -> <invalid>");

                continue;
            }

            var metadata = inner.GetFileMetadata(fileId);
            var samples = new List<string>();

            foreach (var extent in metadata.Extents)
            {
                var bytes = inner.DebugReadPage(extent.Start.Value);
                var sample = bytes.Length > 0 ? bytes[0] : (byte)0;
                samples.Add($"page {extent.Start.Value}:0x{sample:X2}");
            }

            pageSamples.Add($"{path} -> {string.Join(", ", samples)}");
        }

        if (mismatches.Count > 0)
        {
            throw new(
                $"mismatches: {
                    string.Join(", ", mismatches)
                } || metadata: {
                    metadataDump
                } || usage: {
                    usage
                } || duplicates: {
                    duplicatesDump
                } || samples: {
                    string.Join(" | ", pageSamples)
                }");
        }
    }

    [Test]
    public void NestedVirtualFileSystems_Parallel_DebugSnapshot()
    {
        const int layerCount = 4;

        using var root = new TestMemoryStream();
        var fileSystems = new List<VirtualFileSystem>(layerCount);

        Stream current = root;

        for (var layer = 0; layer < layerCount; layer++)
        {
            var vfs = VirtualFileSystem.Mount(current, new() { EnableCompaction = false });
            fileSystems.Add(vfs);

            if (layer < layerCount - 1)
            {
                vfs.CreateDirectory("layers");
                var containerPath = $"layers/layer_{layer}.vfs";
                var containerStream = vfs.OpenFile(containerPath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
                containerStream.SetLength(0);
                current = containerStream;
            }
        }

        var innermost = fileSystems[^1];
        innermost.CreateDirectory("data");

        var baseFileCount = Environment.ProcessorCount;
        var profile = CreateProfile(innermost);
        var fileCount = Math.Max(1, profile.Workload(baseFileCount));
        var payloadLength = profile.Payload(2048);
        var payloads = Enumerable.Range(0, fileCount).Select(i => Enumerable.Repeat((byte)(i % 250 + 1), payloadLength).ToArray()).ToArray();

        var parallelOptions = profile.CreateParallelOptions(baseFileCount);

        Parallel.For(
            0,
            payloads.Length,
            parallelOptions,
            i =>
            {
                var path = $"data/file_{i}.bin";
                using var stream = innermost.OpenFile(path, FileMode.OpenOrCreate, FileAccess.ReadWrite);
                stream.SetLength(0);
                stream.Write(payloads[i], 0, payloads[i].Length);
                stream.Flush();
            });

        var mismatches = new List<string>();

        Parallel.For(
            0,
            payloads.Length,
            parallelOptions,
            i =>
            {
                var path = $"data/file_{i}.bin";
                using var stream = innermost.OpenFile(path, FileMode.Open, FileAccess.Read);
                var buffer = new byte[payloads[i].Length];
                var read = stream.Read(buffer, 0, buffer.Length);

                if (read != buffer.Length || !buffer.AsSpan().SequenceEqual(payloads[i]))
                {
                    var expected = payloads[i][0];
                    var actual = buffer.Length > 0 ? buffer[0] : (byte)0;

                    lock (mismatches)
                        mismatches.Add($"{path}: read={read} first=0x{actual:X2} expected=0x{expected:X2}");
                }
            });

        if (mismatches.Count > 0)
        {
            var metadataDump = string.Join(" | ", innermost.EnumerateDebugFileMetadata());
            var usage = innermost.DescribeDebugUsage();
            var duplicates = string.Join("; ", innermost.FindDebugDuplicatePages().Select(d => $"page={d.Page} owners={string.Join(',', d.Files)}"));
            var pageSamples = new List<string>();

            for (var index = 0; index < payloads.Length; index++)
            {
                var path = $"data/file_{index}.bin";
                var fileId = innermost.ResolveDebugFileId(path);

                if (!fileId.IsValid)
                {
                    pageSamples.Add($"{path} -> invalid");

                    continue;
                }

                var metadata = innermost.GetFileMetadata(fileId);

                foreach (var extent in metadata.Extents)
                {
                    var bytes = innermost.DebugReadPage(extent.Start.Value);
                    pageSamples.Add($"{path} page {extent.Start.Value}:0x{bytes[0]:X2}");
                }
            }

            throw new(
                $"mismatches: {
                    string.Join(" | ", mismatches)
                } || metadata: {
                    metadataDump
                } || usage: {
                    usage
                } || duplicates: {
                    duplicates
                } || samples: {
                    string.Join(" | ", pageSamples)
                }");
        }
    }

    [Test]
    public void NestedVirtualFileSystems_TwoFiles_Sequential_Snapshot()
    {
        using var root = new TestMemoryStream();
        using var outer = VirtualFileSystem.Mount(root, new() { EnableCompaction = false });
        outer.CreateDirectory("layers");

        using var container = outer.OpenFile("layers/inner.vfs", FileMode.OpenOrCreate, FileAccess.ReadWrite);
        container.SetLength(0);

        using var inner = VirtualFileSystem.Mount(container, new() { EnableCompaction = false });
        inner.CreateDirectory("data");

        var payloadA = Enumerable.Repeat((byte)0x11, 4096).ToArray();
        var payloadB = Enumerable.Repeat((byte)0x22, 4096).ToArray();

        using (var streamA = inner.OpenFile("data/a.bin", FileMode.OpenOrCreate, FileAccess.ReadWrite))
        {
            streamA.SetLength(0);
            streamA.Write(payloadA, 0, payloadA.Length);
            streamA.Flush();
        }

        using (var streamB = inner.OpenFile("data/b.bin", FileMode.OpenOrCreate, FileAccess.ReadWrite))
        {
            streamB.SetLength(0);
            streamB.Write(payloadB, 0, payloadB.Length);
            streamB.Flush();
        }

        var idA = inner.ResolveDebugFileId("data/a.bin");
        var idB = inner.ResolveDebugFileId("data/b.bin");
        Assert.That(idA.IsValid, Is.True);
        Assert.That(idB.IsValid, Is.True);

        var metaA = inner.GetFileMetadata(idA);
        var metaB = inner.GetFileMetadata(idB);

        foreach (var extent in metaA.Extents)
        {
            var bytes = inner.DebugReadPage(extent.Start.Value);
            TestContext.WriteLine($"A extent {extent.Start.Value}: first=0x{bytes[0]:X2}");
        }

        foreach (var extent in metaB.Extents)
        {
            var bytes = inner.DebugReadPage(extent.Start.Value);
            TestContext.WriteLine($"B extent {extent.Start.Value}: first=0x{bytes[0]:X2}");
        }

        using (var stream = inner.OpenFile("data/a.bin", FileMode.Open, FileAccess.Read))
        {
            var buffer = new byte[payloadA.Length];
            var read = stream.Read(buffer, 0, buffer.Length);
            TestContext.WriteLine($"A read first=0x{buffer[0]:X2}");
            Assert.That(read, Is.EqualTo(buffer.Length));
            Assert.That(buffer, Is.EqualTo(payloadA));
        }

        using (var stream = inner.OpenFile("data/b.bin", FileMode.Open, FileAccess.Read))
        {
            var buffer = new byte[payloadB.Length];
            var read = stream.Read(buffer, 0, buffer.Length);
            TestContext.WriteLine($"B read first=0x{buffer[0]:X2}");
            Assert.That(read, Is.EqualTo(buffer.Length));
            Assert.That(buffer, Is.EqualTo(payloadB));
        }
    }

    [Test]
    public void NestedVirtualFileSystems_LongRun_ShouldStayConsistent()
    {
        const int layerCount = 4;
        var baseCycles = 3;
        var globalProfile = new StressProfile(PageSizing.DefaultPageSizeOverride ?? PageSizing.DefaultPageSize);
        var cycles = Math.Max(1, globalProfile.Workload(baseCycles));
        var baseFileCount = Math.Min(Environment.ProcessorCount, 10);
        var baseIterationCount = 60;

        for (var cycle = 0; cycle < cycles; cycle++)
        {
            using var root = new TestMemoryStream();
            var fileSystems = new List<VirtualFileSystem>(layerCount);
            var hostStreams = new List<Stream>(layerCount - 1);

            Stream current = root;

            for (var layer = 0; layer < layerCount; layer++)
            {
                var vfs = VirtualFileSystem.Mount(current, new() { EnableCompaction = false });
                fileSystems.Add(vfs);

                if (layer < layerCount - 1)
                {
                    vfs.CreateDirectory("layers");
                    var path = $"layers/layer_{layer}.vfs";
                    var container = vfs.OpenFile(path, FileMode.OpenOrCreate, FileAccess.ReadWrite);
                    container.SetLength(0);
                    hostStreams.Add(container);
                    current = container;
                }
            }

            var innermost = fileSystems[^1];
            innermost.CreateDirectory("data");
            var profile = CreateProfile(innermost);
            var fileCount = Math.Max(1, profile.Workload(baseFileCount));
            var iterationCount = Math.Max(1, profile.Workload(baseIterationCount));
            var pageSize = innermost.PageSize;

            var filePaths = Enumerable.Range(0, fileCount).Select(i => $"data/file_{i}.bin").ToArray();

            foreach (var path in filePaths)
                innermost.CreateFile(path);

            var sequence = 0;
            var lastWrites = new ConcurrentDictionary<int, (int Seq, int Length, byte Value)>();

            var parallelOptions = profile.CreateParallelOptions(baseFileCount);

            Parallel.For(
                0,
                fileCount,
                parallelOptions,
                fileIndex =>
                {
                    var random = new Random(unchecked((cycle + 1) * 7919 + fileIndex));

                    for (var iteration = 0; iteration < iterationCount; iteration++)
                    {
                        var pages = 1 + random.Next(1, 5);
                        var length = pages * pageSize;
                        var value = (byte)(fileIndex * 17 + cycle + iteration + 1);

                        using var stream = innermost.OpenFile(filePaths[fileIndex], FileMode.Open, FileAccess.ReadWrite);
                        stream.SetLength(length);

                        var buffer = ArrayPool<byte>.Shared.Rent(length);

                        try
                        {
                            buffer.AsSpan(0, length).Fill(value);
                            stream.Position = 0;
                            stream.Write(buffer, 0, length);
                            stream.Flush();
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                        }

                        var order = Interlocked.Increment(ref sequence);

                        lastWrites.AddOrUpdate(
                            fileIndex,
                            (order, length, value),
                            (_, existing) => existing.Seq > order ? existing : (order, length, value));
                    }
                });

            for (var i = 0; i < fileCount; i++)
            {
                using var stream = innermost.OpenFile(filePaths[i], FileMode.Open, FileAccess.Read);
                var expected = lastWrites[i];
                Assert.That(stream.Length, Is.EqualTo(expected.Length), $"Cycle {cycle} file {filePaths[i]} length mismatch");

                if (stream.Length == 0)
                    continue;

                var buffer = new byte[stream.Length];
                var read = stream.Read(buffer, 0, buffer.Length);
                Assert.That(read, Is.EqualTo(buffer.Length));
                var expectedBuffer = new byte[buffer.Length];
                expectedBuffer.AsSpan().Fill(expected.Value);
                Assert.That(buffer, Is.EqualTo(expectedBuffer), $"Cycle {cycle} file {filePaths[i]} byte mismatch");
            }

            for (var index = fileSystems.Count - 1; index >= 0; index--)
                fileSystems[index].Dispose();

            for (var index = hostStreams.Count - 1; index >= 0; index--)
                hostStreams[index].Dispose();

            Assert.That(root.Length, Is.GreaterThan(0), $"Cycle {cycle} root stream empty");
        }
    }

    [Test, NonParallelizable]
    public void CompactionEngine_ShouldHandleConcurrentWrites()
    {
        using var memory = new TestMemoryStream();
        using var vfs = VirtualFileSystem.Mount(memory, new() { EnableCompaction = true });

        vfs.CreateDirectory("data");
        var path = "data/compact.bin";
        vfs.CreateFile(path);
        var fileId = vfs.ResolveDebugFileId(path);
        var pageSize = vfs.PageSize;
        var profile = CreateProfile(vfs);

        var baseWriters = Math.Min(Environment.ProcessorCount, 6);
        var writerCount = Math.Max(1, profile.Workload(baseWriters));
        var iterations = Math.Max(1, profile.Workload(80));
        var parallelOptions = profile.CreateParallelOptions(baseWriters);
        var sequence = 0;
        var states = new ConcurrentDictionary<int, (int Seq, int Length, byte Value)>();

        Parallel.For(
            0,
            writerCount,
            parallelOptions,
            writer =>
            {
                var random = new Random(unchecked(writer * 7919 + iterations));

                for (var iteration = 0; iteration < iterations; iteration++)
                {
                    var pages = 1 + random.Next(1, 5);
                    var length = pages * pageSize;
                    var value = (byte)(writer * 23 + iteration + 5);

                    using var stream = vfs.OpenFile(path, FileMode.Open, FileAccess.ReadWrite);
                    stream.SetLength(length);

                    var buffer = ArrayPool<byte>.Shared.Rent(length);

                    try
                    {
                        buffer.AsSpan(0, length).Fill(value);
                        stream.Position = 0;
                        stream.Write(buffer, 0, length);
                        stream.Flush();
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }

                    var order = Interlocked.Increment(ref sequence);
                    states.AddOrUpdate(writer, (order, length, value), (_, existing) => existing.Seq > order ? existing : (order, length, value));

                    if (iteration % 10 == 0)
                        vfs.RunCompaction();
                }
            });

        Thread.Sleep(200);
        vfs.RunCompaction();

        var expected = states.Values.OrderByDescending(s => s.Seq).First();

        using (var stream = vfs.OpenFile(path, FileMode.Open, FileAccess.Read))
        {
            Assert.That(stream.Length, Is.EqualTo(expected.Length));

            if (expected.Length > 0)
            {
                var buffer = new byte[stream.Length];
                var read = stream.Read(buffer, 0, buffer.Length);
                Assert.That(read, Is.EqualTo(buffer.Length));
                var expectedBuffer = new byte[buffer.Length];
                expectedBuffer.AsSpan().Fill(expected.Value);
                Assert.That(buffer, Is.EqualTo(expectedBuffer));
            }
        }

        var metadata = vfs.GetFileMetadata(fileId);
        Assert.That(metadata.Extents.Length, Is.EqualTo(1));
    }

    [Test, NonParallelizable]
    public void CompactionEngine_TruncateStorm_ShouldKeepData()
    {
        using var memory = new TestMemoryStream();
        using var vfs = VirtualFileSystem.Mount(memory, new() { EnableCompaction = true });

        vfs.CreateDirectory("data");
        const string path = "data/storm.bin";
        vfs.CreateFile(path);
        var fileId = vfs.ResolveDebugFileId(path);
        var pageSize = vfs.PageSize;
        var profile = CreateProfile(vfs);
        var baseWorkers = Math.Min(Environment.ProcessorCount * 2, 12);
        var workerCount = Math.Max(1, profile.Workload(baseWorkers));
        var iterations = Math.Max(1, profile.Workload(80));
        var parallelOptions = profile.CreateParallelOptions(baseWorkers);

        var lastState = new ConcurrentDictionary<int, (int Seq, int Length, byte Value)>();
        var sequence = 0;

        Parallel.For(
            0,
            workerCount,
            parallelOptions,
            worker =>
            {
                var random = new Random(unchecked(worker * 7919 + iterations));

                for (var iteration = 0; iteration < iterations; iteration++)
                {
                    using var stream = vfs.OpenFile(path, FileMode.Open, FileAccess.ReadWrite);

                    var length = random.Next(1, 5) * pageSize;
                    var value = (byte)(worker * 31 + iteration + 3);

                    stream.SetLength(length);

                    if (length > 0)
                    {
                        var buffer = ArrayPool<byte>.Shared.Rent(length);

                        try
                        {
                            buffer.AsSpan(0, length).Fill(value);
                            stream.Position = 0;
                            stream.Write(buffer, 0, length);
                            stream.Flush();
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                        }
                    }
                    else
                        stream.Flush();

                    var order = Interlocked.Increment(ref sequence);
                    var newState = (Seq: order, Length: length, Value: value);
                    lastState.AddOrUpdate(0, newState, (_, existing) => existing.Seq > newState.Seq ? existing : newState);

                    if (iteration % 4 == 0)
                    {
                        stream.SetLength(0);
                        stream.Flush();
                        var clearOrder = Interlocked.Increment(ref sequence);
                        var cleared = (Seq: clearOrder, Length: 0, Value: (byte)0);
                        lastState.AddOrUpdate(0, cleared, (_, existing) => existing.Seq > cleared.Seq ? existing : cleared);
                    }

                    if (iteration % 5 == 0)
                        vfs.RunCompaction();
                }
            });

        for (var i = 0; i < 5; i++)
            vfs.RunCompaction();

        var expected = lastState.TryGetValue(0, out var state) ? state : (Seq: 0, Length: 0, Value: (byte)0);

        using (var stream = vfs.OpenFile(path, FileMode.Open, FileAccess.Read))
        {
            Assert.That(stream.Length, Is.EqualTo(expected.Length));

            if (expected.Length > 0)
            {
                var buffer = new byte[expected.Length];
                var read = stream.Read(buffer, 0, buffer.Length);
                Assert.That(read, Is.EqualTo(buffer.Length));

                var expectedBuffer = new byte[buffer.Length];
                expectedBuffer.AsSpan().Fill(expected.Value);
                Assert.That(buffer, Is.EqualTo(expectedBuffer));
            }
        }

        var metadata = vfs.GetFileMetadata(fileId);
        Assert.That(metadata.Extents.Length, Is.AtMost(1));
    }

    [Test]
    public void CrashSnapshots_ShouldRemainMountable()
    {
        const int fileCount = 3;
        using var memory = new TestMemoryStream();
        using var vfs = VirtualFileSystem.Mount(memory, new() { EnableCompaction = true });

        vfs.CreateDirectory("data");
        var pageSize = vfs.PageSize;
        var paths = Enumerable.Range(0, fileCount).Select(i => $"data/file_{i}.bin").ToArray();

        foreach (var path in paths)
            vfs.CreateFile(path);

        var snapshots = new List<(byte[] Image, Dictionary<string, (long Length, byte FirstByte)> State)>();

        void snapshot() => snapshots.Add((memory.ToArray(), CaptureFileStates(vfs, paths)));

        snapshot();

        var iterations = 12;

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            for (var index = 0; index < fileCount; index++)
            {
                var lengthPages = (iteration + index) % 4 + 1;
                var length = lengthPages * pageSize;
                var value = (byte)(index * 33 + iteration + 1);

                using var stream = vfs.OpenFile(paths[index], FileMode.Open, FileAccess.ReadWrite);
                stream.SetLength(length);

                if (length > 0)
                {
                    var buffer = new byte[length];
                    buffer.AsSpan().Fill(value);
                    stream.Position = 0;
                    stream.Write(buffer, 0, length);
                }

                stream.Flush();
                snapshot();
            }

            vfs.RunCompaction();
            snapshot();
        }

        foreach (var (image, expectedState) in snapshots)
        {
            using var snapshotStream = new TestMemoryStream(image);
            using var replay = VirtualFileSystem.Mount(snapshotStream);

            foreach (var (path, state) in expectedState)
            {
                Assert.That(replay.FileExists(path), Is.True, $"Snapshot missing file {path}");
                using var stream = replay.OpenFile(path, FileMode.Open, FileAccess.Read);
                Assert.That(stream.Length, Is.EqualTo(state.Length), $"Snapshot mismatch length for {path}");

                if (state.Length == 0)
                    continue;

                var first = stream.ReadByte();
                Assert.That(first, Is.EqualTo(state.FirstByte), $"Snapshot mismatch content for {path}");
            }
        }
    }

    private static Dictionary<string, (long Length, byte FirstByte)> CaptureFileStates(VirtualFileSystem vfs, IEnumerable<string> paths)
    {
        var result = new Dictionary<string, (long Length, byte FirstByte)>();

        foreach (var path in paths)
        {
            using var stream = vfs.OpenFile(path, FileMode.Open, FileAccess.Read);
            var length = stream.Length;
            byte firstByte = 0;

            if (length > 0)
                firstByte = (byte)stream.ReadByte();

            result[path] = (length, firstByte);
        }

        return result;
    }

    [Test, NonParallelizable]
    public void NestedVirtualFileSystems_WithCompaction_ShouldStayConsistent()
    {
        using var root = new TestMemoryStream();
        using var outer = VirtualFileSystem.Mount(root, new() { EnableCompaction = false });
        outer.CreateDirectory("layers");

        using var container = outer.OpenFile("layers/inner.vfs", FileMode.Create, FileAccess.ReadWrite);
        container.SetLength(0);

        using var inner = VirtualFileSystem.Mount(container, new() { EnableCompaction = true });
        inner.CreateDirectory("data");

        var profile = CreateProfile(inner);
        var baseFileCount = Math.Min(Environment.ProcessorCount, 8);
        var fileCount = Math.Max(1, profile.Workload(baseFileCount));
        var payloadLength = Math.Max(1, profile.Payload(Math.Max(inner.PageSize / 2, 16)));
        var payloads = Enumerable.Range(0, fileCount).Select(i => Enumerable.Repeat((byte)(i + 1), payloadLength).ToArray()).ToArray();

        var parallelOptions = profile.CreateParallelOptions(baseFileCount);

        Parallel.For(
            0,
            fileCount,
            parallelOptions,
            i =>
            {
                var path = $"data/file_{i}.bin";
                using var stream = inner.OpenFile(path, FileMode.OpenOrCreate, FileAccess.ReadWrite);
                stream.SetLength(0);
                stream.Write(payloads[i], 0, payloads[i].Length);
                stream.Flush();
                inner.RunCompaction();
            });

        inner.RunCompaction();

        for (var i = 0; i < fileCount; i++)
        {
            var path = $"data/file_{i}.bin";
            using var stream = inner.OpenFile(path, FileMode.Open, FileAccess.Read);
            var buffer = new byte[payloads[i].Length];
            var read = stream.Read(buffer, 0, buffer.Length);
            Assert.That(read, Is.EqualTo(buffer.Length));
            Assert.That(buffer, Is.EqualTo(payloads[i]));
        }
    }

    [Test]
    public void StorageEngine_ShouldHandleShortReads()
    {
        using var memory = new TestMemoryStream();
        using var shortStream = new ShortReadStream(memory);
        using var vfs = VirtualFileSystem.Mount(shortStream, new());

        const string path = "data.bin";
        vfs.CreateFile(path);

        var payload = Enumerable.Range(0, 512).Select(i => (byte)(i % 251)).ToArray();

        using (var stream = vfs.OpenFile(path, FileMode.Open, FileAccess.ReadWrite))
        {
            for (var i = 0; i < 12; i++)
                stream.Write(payload, 0, payload.Length);
        }

        using (var stream = vfs.OpenFile(path, FileMode.Open, FileAccess.Read))
        {
            var buffer = new byte[512 * 12];
            var read = 0;

            while (read < buffer.Length)
            {
                var delta = stream.Read(buffer, read, buffer.Length - read);

                if (delta == 0)
                    break;

                read += delta;
            }

            Assert.That(read, Is.EqualTo(buffer.Length));

            var expected = new byte[buffer.Length];

            for (var i = 0; i < 12; i++)
                Array.Copy(payload, 0, expected, i * payload.Length, payload.Length);

            Assert.That(buffer, Is.EqualTo(expected));
        }
    }

    [Test]
    public void VirtualFileSystem_ShouldSupportVariousPageSizes()
    {
        int[] pageSizes = [4 * 1024, PageSizing.DefaultPageSize, 1 * 1024 * 1024];

        foreach (var pageSize in pageSizes)
        {
            using var memory = new TestMemoryStream();
            using var vfs = VirtualFileSystem.Mount(memory, new() { PageSize = pageSize, EnableCompaction = true });
            vfs.CreateDirectory("payloads");

            var random = new Random(pageSize);

            for (var fileIndex = 0; fileIndex < 4; fileIndex++)
            {
                var path = $"payloads/file_{fileIndex}.bin";
                var length = pageSize + fileIndex * pageSize / 2;
                var payload = new byte[length];
                random.NextBytes(payload);

                using (var writer = vfs.OpenFile(path, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                {
                    writer.SetLength(0);
                    writer.Write(payload, 0, payload.Length);
                    writer.Flush();
                }

                using (var reader = vfs.OpenFile(path, FileMode.Open, FileAccess.Read))
                {
                    var buffer = new byte[length];
                    var read = reader.Read(buffer, 0, buffer.Length);
                    Assert.That(read, Is.EqualTo(buffer.Length));
                    Assert.That(buffer, Is.EqualTo(payload), $"PageSize {pageSize} mismatch for {path}");
                }
            }
        }
    }

    private readonly struct StressProfile
    {
        internal StressProfile(int pageSize)
        {
            this.PageSize = pageSize;
            this.Scale = pageSize >= 4096 ? 1 : Math.Max(1, 4096 / Math.Max(pageSize, 1));
        }

        internal int PageSize { get; }
        internal int Scale { get; }
        private bool IsScaled => this.Scale > 1;

        internal int Workload(int baseValue, int minimum = 1)
        {
            if (!this.IsScaled)
                return Math.Max(minimum, baseValue);

            var scaled = (int)Math.Ceiling(baseValue / (double)this.Scale);

            return Math.Max(minimum, scaled);
        }

        internal int Payload(int baseValue, int minimum = 16)
        {
            if (!this.IsScaled)
                return Math.Max(minimum, baseValue);

            var scaled = (int)Math.Ceiling(baseValue / (double)this.Scale);
            var lowerBound = Math.Max(minimum, Math.Max(16, this.PageSize));

            return Math.Clamp(scaled, lowerBound, baseValue);
        }

        internal ParallelOptions CreateParallelOptions(int baseMaxDegree)
        {
            var max = this.Workload(baseMaxDegree, 1);

            return new() { MaxDegreeOfParallelism = Math.Max(1, Math.Min(baseMaxDegree, max)) };
        }

        internal int TimeBudgetMs(int baseMilliseconds, int maxMultiplier = 16)
        {
            if (!this.IsScaled)
                return baseMilliseconds;

            var multiplier = Math.Min(this.Scale, maxMultiplier);

            return baseMilliseconds * multiplier;
        }
    }

    private sealed class ShortReadStream(Stream inner, int maxChunk = 4096) : Stream
    {
        private readonly Stream inner = inner ?? throw new ArgumentNullException(nameof(inner));
        private readonly int maxChunk = Math.Max(1, maxChunk);
        private readonly Random random = new(17);

        /// <inheritdoc />
        public override bool CanRead => this.inner.CanRead;

        /// <inheritdoc />
        public override bool CanSeek => this.inner.CanSeek;

        /// <inheritdoc />
        public override bool CanWrite => this.inner.CanWrite;

        /// <inheritdoc />
        public override long Length => this.inner.Length;

        /// <inheritdoc />
        public override long Position
        {
            get => this.inner.Position;
            set => this.inner.Position = value;
        }

        /// <inheritdoc />
        public override void Flush() => this.inner.Flush();

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count == 0)
                return 0;

            var chunk = Math.Min(count, this.maxChunk);
            var slice = this.random.Next(1, chunk + 1);

            return this.inner.Read(buffer, offset, slice);
        }

        /// <inheritdoc />
        public override int Read(Span<byte> destination)
        {
            if (destination.Length == 0)
                return 0;

            var chunk = Math.Min(destination.Length, this.maxChunk);
            var slice = this.random.Next(1, chunk + 1);

            return this.inner.Read(destination[..slice]);
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => this.inner.Seek(offset, origin);

        /// <inheritdoc />
        public override void SetLength(long value) => this.inner.SetLength(value);

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => this.inner.Write(buffer, offset, count);

        /// <inheritdoc />
        public override void Write(ReadOnlySpan<byte> buffer) => this.inner.Write(buffer);

        /// <inheritdoc />
        public override void WriteByte(byte value) => this.inner.WriteByte(value);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                this.inner.Dispose();

            base.Dispose(disposing);
        }
    }
}
