using System.Collections.Concurrent;

namespace Itexoft.Common.IO;

internal sealed class TempFile : IDisposable, IAsyncDisposable
{
    private static readonly ConcurrentDictionary<string, object> tempFiles = [];

    static TempFile()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            foreach (var file in tempFiles.Keys)
                Delete(file);
        };
    }

    private int disposed;

    public string FilePath { get; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public TempFile() => tempFiles.TryAdd(this.FilePath, this);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) == 1)
            return;
        try
        {
            if (File.Exists(this.FilePath))
                File.Delete(this.FilePath);
            tempFiles.TryRemove(this.FilePath, out _);
        }
        catch
        {
            Interlocked.Exchange(ref this.disposed, 0);
            throw;
        }
    }

    private static void Delete(string file)
    {
        try
        {
            if (File.Exists(file))
                File.Delete(file);
        }
        catch { }
    }

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

    public static implicit operator string(TempFile file) => file.FilePath;
}