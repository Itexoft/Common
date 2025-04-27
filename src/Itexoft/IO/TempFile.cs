// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Concurrent;
using Itexoft.Core;

namespace Itexoft.IO;

public sealed class TempFile : IDisposable, IAsyncDisposable
{
    private static readonly ConcurrentDictionary<string, object> tempFiles = [];

    private Disposed disposed;

    static TempFile() => AppDomain.CurrentDomain.ProcessExit += (_, _) =>
    {
        foreach (var file in tempFiles.Keys)
            Delete(file);
    };

    public TempFile(string? extension = null)
    {
        this.FilePath = Path.Combine(Path.GetTempPath(), Path.ChangeExtension(Guid.NewGuid().ToString(), extension));
        tempFiles.TryAdd(this.FilePath, this);
    }

    public string FilePath { get; }

    public ValueTask DisposeAsync()
    {
        try
        {
            this.Dispose();

            return ValueTask.CompletedTask;
        }
        catch (Exception ex)
        {
            return ValueTask.FromException(ex);
        }
    }

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        if (File.Exists(this.FilePath))
            File.Delete(this.FilePath);

        tempFiles.TryRemove(this.FilePath, out _);
    }

    public async Task WriteAllTextAsync(string text) => await File.WriteAllTextAsync(this.FilePath, text);
    public void WriteAllText(string text) => File.WriteAllText(this.FilePath, text);

    private static void Delete(string file)
    {
        try
        {
            if (File.Exists(file))
                File.Delete(file);
        }
        catch { }
    }

    public static implicit operator string(TempFile file) => file.FilePath;
    public override string ToString() => this.FilePath;
}
