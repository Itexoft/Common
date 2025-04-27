using System.Collections.Concurrent;

namespace Itexoft.Common.IO;

public sealed class TempFile : IDisposable, IAsyncDisposable
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

    public string FilePath { get; }

    public TempFile(string? extension = null)
    {
        this.FilePath = Path.Combine(Path.GetTempPath(), Path.ChangeExtension(Guid.NewGuid().ToString(), extension));
        tempFiles.TryAdd(this.FilePath, this);
    }

    public async Task WriteAllTextAsync(string text) => await File.WriteAllTextAsync(this.FilePath, text);
    public void WriteAllText(string text) => File.WriteAllText(this.FilePath, text);

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
    public override string ToString() => this.FilePath;
}