namespace Itexoft.Common;

public static class CommonExtensions
{
    public static async Task WriteAsync(
        this TextWriter writer,
        TextReader reader,
        int cacheSize = 1,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        for (var buffer = new char[cacheSize]; !cancellationToken.IsCancellationRequested;)
        {
            var read = await reader.ReadAsync(buffer, 0, buffer.Length);
            if (read == 0)
            {
                if (reader.Peek() == -1)
                    break;

                await Task.Yield();

                continue;
            }

            for (var i = 0; i < read; i++)
                await writer.WriteAsync(buffer[i]);
        }
    }
}