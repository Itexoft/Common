// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Itexoft.IO.VFS;

const int defaultDurationSeconds = 60;
const int defaultFileCount = 16;
const int defaultWorkers = 8;

var durationSeconds = GetIntArgument("--duration", defaultDurationSeconds);
var fileCount = GetIntArgument("--files", defaultFileCount);
var workerCount = GetIntArgument("--workers", Math.Min(Environment.ProcessorCount * 2, defaultWorkers));
var outputPath = GetStringArgument("--output", string.Empty);

var tempPath = Path.Combine(Path.GetTempPath(), $"virtualio_load_{Process.GetCurrentProcess().Id}_{Guid.NewGuid():N}.vfs");
if (File.Exists(tempPath))
    File.Delete(tempPath);

await using var backing = new FileStream(
    tempPath,
    FileMode.Create,
    FileAccess.ReadWrite,
    FileShare.ReadWrite,
    1 << 20,
    FileOptions.Asynchronous | FileOptions.RandomAccess | FileOptions.DeleteOnClose);

var options = new VirtualFileSystemOptions
{
    EnableCompaction = true
};

using var vfs = VirtualFileSystem.Mount(backing, options);
var runCompaction = typeof(VirtualFileSystem)
    .GetMethod("RunCompaction", BindingFlags.NonPublic | BindingFlags.Instance);
var pageSizeField = typeof(VirtualFileSystem)
    .GetField("pageSize", BindingFlags.NonPublic | BindingFlags.Instance);
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
    workers[i] = Task.Run(() => Worker(vfs, runCompaction, pageSize, workerId, fileCount, stats, cts.Token));
}

var stopwatch = Stopwatch.StartNew();
await Task.WhenAll(workers);
stopwatch.Stop();

if (File.Exists(tempPath))
    try
    {
        File.Delete(tempPath);
    }
    catch
    {
        /* ignore */
    }

var summary = stats.Snapshot(stopwatch.Elapsed, workers.Length);
Console.WriteLine(summary);

if (!string.IsNullOrWhiteSpace(outputPath))
{
    await File.WriteAllTextAsync(outputPath, summary);
    Console.WriteLine($"Summary saved to {outputPath}");
}

static void Worker(
    VirtualFileSystem vfs,
    MethodInfo? runCompaction,
    int pageSize,
    int workerId,
    int fileCount,
    LoadStats stats,
    CancellationToken token)
{
    var random = new Random(unchecked(workerId * 7919 + Environment.TickCount));
    var buffer = ArrayPool<byte>.Shared.Rent(pageSize * 8);

    try
    {
        var iteration = 0;
        while (!token.IsCancellationRequested)
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

static int GetIntArgument(string name, int defaultValue)
{
    var args = Environment.GetCommandLineArgs();

    for (var i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out var parsed))
            return parsed;

    return defaultValue;
}

static string GetStringArgument(string name, string defaultValue)
{
    var args = Environment.GetCommandLineArgs();

    for (var i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];

    return defaultValue;
}

internal sealed class LoadStats
{
    private readonly ConcurrentDictionary<string, long> _errorHistogram = new();
    private long _compactions;
    private long _errors;
    private long _readBytes;
    private long _reads;
    private long _truncates;
    private long _writeBytes;
    private long _writes;

    public void IncrementWrites(int bytes)
    {
        Interlocked.Increment(ref this._writes);
        Interlocked.Add(ref this._writeBytes, bytes);
    }

    public void IncrementReads(int bytes)
    {
        Interlocked.Increment(ref this._reads);
        Interlocked.Add(ref this._readBytes, bytes);
    }

    public void IncrementTruncates() => Interlocked.Increment(ref this._truncates);
    public void IncrementCompactions() => Interlocked.Increment(ref this._compactions);

    public void IncrementErrors(Exception? ex)
    {
        Interlocked.Increment(ref this._errors);
        if (ex != null)
        {
            var key = ex.GetType().Name;
            this._errorHistogram.AddOrUpdate(key, 1, static (_, count) => count + 1);
            Debug.WriteLine(ex);
        }
    }

    public string Snapshot(TimeSpan elapsed, int workers)
    {
        var writeBytes = Interlocked.Read(ref this._writeBytes);
        var readBytes = Interlocked.Read(ref this._readBytes);
        var errorInfo = this._errorHistogram.Count == 0
            ? ""
            : $" (types: {string.Join(", ", this._errorHistogram.OrderByDescending(k => k.Value).Select(k => $"{k.Key}={k.Value}"))})";

        return
            $"Duration: {elapsed}; Workers: {workers}; Writes: {Interlocked.Read(ref this._writes)} ({BytesToString(writeBytes)}); Reads: {Interlocked.Read(ref this._reads)} ({BytesToString(readBytes)}); Truncates: {Interlocked.Read(ref this._truncates)}; Compactions: {Interlocked.Read(ref this._compactions)}; Errors: {Interlocked.Read(ref this._errors)}{errorInfo}";
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