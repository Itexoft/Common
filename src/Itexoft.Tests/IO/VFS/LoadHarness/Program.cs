// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Itexoft.IO.VFS;
using Itexoft.Threading;

const int defaultDurationSeconds = 60;
const int defaultFileCount = 16;
const int defaultWorkers = 8;

var durationSeconds = getIntArgument("--duration", defaultDurationSeconds);
var fileCount = getIntArgument("--files", defaultFileCount);
var workerCount = getIntArgument("--workers", Math.Min(Environment.ProcessorCount * 2, defaultWorkers));
var outputPath = getStringArgument("--output", string.Empty);

var tempPath = Path.Combine(Path.GetTempPath(), $"virtualio_load_{Process.GetCurrentProcess().Id}_{Guid.NewGuid():N}.vfs");

if (File.Exists(tempPath))
    File.Delete(tempPath);

var backing = new FileStream(
    tempPath,
    FileMode.Create,
    FileAccess.ReadWrite,
    FileShare.ReadWrite,
    1 << 20,
    FileOptions.Asynchronous | FileOptions.RandomAccess | FileOptions.DeleteOnClose);

await using var backing1 = backing.ConfigureAwait(false);

var options = new VirtualFileSystemOptions
{
    EnableCompaction = true,
};

using var vfs = VirtualFileSystem.Mount(backing, options);
var runCompaction = typeof(VirtualFileSystem).GetMethod("RunCompaction", BindingFlags.NonPublic | BindingFlags.Instance);
var pageSizeField = typeof(VirtualFileSystem).GetField("pageSize", BindingFlags.NonPublic | BindingFlags.Instance);
var pageSize = pageSizeField is not null ? (int)pageSizeField.GetValue(vfs)! : 64 * 1024;

vfs.CreateDirectory("data");

for (var i = 0; i < fileCount; i++)
    vfs.CreateFile($"data/file_{i:D4}.bin");

var stats = new LoadStats();
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds));
var workers = new Task[Math.Max(1, workerCount)];

for (var i = 0; i < workers.Length; i++)
{
    var workerId = i;
    workers[i] = Task.Run(() => worker(vfs, runCompaction, pageSize, workerId, fileCount, stats, cts.Token));
}

var stopwatch = Stopwatch.StartNew();
await Task.WhenAll(workers).ConfigureAwait(false);
stopwatch.Stop();

if (File.Exists(tempPath))
{
    try
    {
        File.Delete(tempPath);
    }
    catch
    {
        /* ignore */
    }
}

var summary = stats.Snapshot(stopwatch.Elapsed, workers.Length);
Console.WriteLine(summary);

if (!string.IsNullOrWhiteSpace(outputPath))
{
    await File.WriteAllTextAsync(outputPath, summary).ConfigureAwait(false);
    Console.WriteLine($"Summary saved to {outputPath}");
}

static void worker(VirtualFileSystem vfs, MethodInfo? runCompaction, int pageSize, int workerId, int fileCount, LoadStats stats, CancelToken token)
{
    var random = new Random(unchecked(workerId * 7919 + Environment.TickCount));
    var buffer = ArrayPool<byte>.Shared.Rent(pageSize * 8);

    try
    {
        var iteration = 0;

        while (!token.IsRequested)
        {
            var fileIndex = random.Next(fileCount);
            var path = $"data/file_{fileIndex:D4}.bin";

            try
            {
                using var stream = vfs.OpenFile(path, FileMode.Open, FileAccess.ReadWrite);
                var op = random.Next(0, 4);

                switch (op)
                {
                    case 0:
                    case 1:
                    {
                        var pages = random.Next(1, 8);
                        var length = pages * pageSize;
                        stream.SetLength(length);
                        stream.Position = 0;
                        random.NextBytes(buffer.AsSpan(0, length));
                        stream.Write(buffer, 0, length);
                        stream.Flush();
                        stats.IncrementWrites(length);

                        break;
                    }
                    case 2:
                    {
                        var newLength = random.Next(0, 8) * pageSize;
                        stream.SetLength(newLength);
                        stats.IncrementTruncates();

                        break;
                    }
                    default:
                    {
                        stream.Position = 0;
                        var totalRead = 0;
                        var remaining = (int)Math.Min(stream.Length, buffer.Length);
                        int bytesRead;

                        while (remaining > 0 && (bytesRead = stream.Read(buffer, 0, remaining)) > 0)
                        {
                            totalRead += bytesRead;
                            remaining = (int)Math.Min(stream.Length - totalRead, buffer.Length);

                            if (remaining <= 0)
                                break;
                        }

                        stats.IncrementReads(totalRead);

                        break;
                    }
                }

                if ((++iteration & 0x1F) == 0 && runCompaction is not null)
                {
                    runCompaction.Invoke(vfs, null);
                    stats.IncrementCompactions();
                }
            }
            catch (Exception ex)
            {
                stats.IncrementErrors(ex);
                Console.WriteLine($"[worker {workerId}] {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(buffer);
    }
}

static int getIntArgument(string name, int defaultValue)
{
    var args = Environment.GetCommandLineArgs();

    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out var parsed))
            return parsed;
    }

    return defaultValue;
}

static string getStringArgument(string name, string defaultValue)
{
    var args = Environment.GetCommandLineArgs();

    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    }

    return defaultValue;
}

internal sealed class LoadStats
{
    private readonly ConcurrentDictionary<string, long> errorHistogram = new();
    private long compactions;
    private long errors;
    private long readBytes;
    private long reads;
    private long truncates;
    private long writeBytes;
    private long writes;

    public void IncrementWrites(int bytes)
    {
        Interlocked.Increment(ref this.writes);
        Interlocked.Add(ref this.writeBytes, bytes);
    }

    public void IncrementReads(int bytes)
    {
        Interlocked.Increment(ref this.reads);
        Interlocked.Add(ref this.readBytes, bytes);
    }

    public void IncrementTruncates() => Interlocked.Increment(ref this.truncates);
    public void IncrementCompactions() => Interlocked.Increment(ref this.compactions);

    public void IncrementErrors(Exception? ex)
    {
        Interlocked.Increment(ref this.errors);

        if (ex != null)
        {
            var key = ex.GetType().Name;
            this.errorHistogram.AddOrUpdate(key, 1, static (_, count) => count + 1);
            Debug.WriteLine(ex);
        }
    }

    public string Snapshot(TimeSpan elapsed, int workers)
    {
        var writeBytes = Interlocked.Read(ref this.writeBytes);
        var readBytes = Interlocked.Read(ref this.readBytes);

        var errorInfo = this.errorHistogram.Count == 0
            ? ""
            : $" (types: {string.Join(", ", this.errorHistogram.OrderByDescending(k => k.Value).Select(k => $"{k.Key}={k.Value}"))})";

        return $"Duration: {
            elapsed
        }; Workers: {
            workers
        }; Writes: {
            Interlocked.Read(ref this.writes)
        } ({
            BytesToString(writeBytes)
        }); Reads: {
            Interlocked.Read(ref this.reads)
        } ({
            BytesToString(readBytes)
        }); Truncates: {
            Interlocked.Read(ref this.truncates)
        }; Compactions: {
            Interlocked.Read(ref this.compactions)
        }; Errors: {
            Interlocked.Read(ref this.errors)
        }{
            errorInfo
        }";
    }

    private static string BytesToString(long bytes)
    {
        if (bytes > 1L << 30)
            return $"{bytes / (double)(1L << 30):F2} GiB";

        if (bytes > 1L << 20)
            return $"{bytes / (double)(1L << 20):F2} MiB";

        if (bytes > 1L << 10)
            return $"{bytes / (double)(1L << 10):F2} KiB";

        return $"{bytes} B";
    }
}
