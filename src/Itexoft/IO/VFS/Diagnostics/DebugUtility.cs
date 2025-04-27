// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics;
using System.Globalization;
using Itexoft.IO.VFS.Core;
using Itexoft.IO.VFS.Metadata.Models;
using Itexoft.IO.VFS.Storage;

namespace Itexoft.IO.VFS.Diagnostics;

internal static class DebugUtility
{
    private static readonly string? logSetting = Environment.GetEnvironmentVariable("VFS_DEBUG_LOG");
    private static readonly bool verbose = !string.IsNullOrWhiteSpace(logSetting);
    private static readonly Lazy<TextWriter?> writer = new(CreateWriter, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly object disposeGate = new();
    private static bool disposed;

    static DebugUtility()
    {
        if (!verbose)
            return;

        AppDomain.CurrentDomain.ProcessExit += (_, _) => DisposeWriter();
        AppDomain.CurrentDomain.DomainUnload += (_, _) => DisposeWriter();
    }

    public static bool Enabled => verbose || Debugger.IsAttached;

    [Conditional("DEBUG")]
    public static void Log(string message)
    {
        if (!Enabled)
            return;

        var target = writer.Value;
        if (target is null)
        {
            Debug.WriteLine(message);

            return;
        }

        target.WriteLine(message);
    }

    [Conditional("DEBUG")]
    public static void Break(string reason) => DebugBreakHelper.Trigger(reason);

    [Conditional("DEBUG")]
    public static void RecordWrite(
        StorageEngine storage,
        FileId fileId,
        PageId pageId,
        ReadOnlySpan<byte> buffer,
        string context,
        string owner) =>
        DebugPageTracker.RecordWrite(storage, fileId, pageId, buffer, context, owner);

    [Conditional("DEBUG")]
    public static void Audit(StorageEngine storage, string scope) => DebugPageTracker.Audit(storage, scope);

    private static TextWriter? CreateWriter()
    {
        if (!verbose)
            return null;

        try
        {
            var path = ResolveLogPath();

            if (path is null)
                return null;

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            var textWriter = TextWriter.Synchronized(
                new StreamWriter(stream)
                {
                    AutoFlush = true
                });
            textWriter.WriteLine(
                $"[DebugUtility] logging started {DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)} pid={Environment.ProcessId}");

            return textWriter;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DebugUtility] Failed to create log writer: {ex}");

            return null;
        }
    }

    private static string? ResolveLogPath()
    {
        if (string.IsNullOrWhiteSpace(logSetting))
            return null;

        if (string.Equals(logSetting, "1", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = $"vfs_debug_{Environment.ProcessId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log";

            return Path.Combine(Path.GetTempPath(), fileName);
        }

        return logSetting;
    }

    private static void DisposeWriter()
    {
        lock (disposeGate)
        {
            if (disposed)
                return;

            if (writer.IsValueCreated)
                try
                {
                    writer.Value?.Flush();
                    writer.Value?.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DebugUtility] Failed to dispose writer: {ex}");
                }

            disposed = true;
        }
    }
}

internal readonly struct DebugScope : IDisposable
{
    private readonly IDisposable? inner;

    private DebugScope(IDisposable? inner) => this.inner = inner;

    public static DebugScope Begin(string name)
    {
        if (!DebugUtility.Enabled)
            return default;

        return new(DebugIoScope.Begin(name));
    }

    public void Dispose() => this.inner?.Dispose();
}