// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Reflection;

namespace Itexoft.EmbeddedWeb;

/// <summary>
/// Абстракция источника архива статических файлов. Поток должен поддерживать перемотку.
/// </summary>
public interface IEmbeddedArchiveSource
{
    ValueTask<Stream> OpenArchiveAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Фабрики источников архивов.
/// </summary>
public static class EmbeddedArchiveSource
{
    public static IEmbeddedArchiveSource FromFactory(Func<CancellationToken, ValueTask<Stream>> factory)
        => new DelegateArchiveSource(factory);

    public static IEmbeddedArchiveSource FromStream(Stream stream, bool leaveOpen = false)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));
        var memory = new MemoryStream();
        stream.CopyTo(memory);
        if (!leaveOpen)
            stream.Dispose();
        memory.Seek(0, SeekOrigin.Begin);
        var buffer = memory.ToArray();

        return new DelegateArchiveSource(_ => new(new MemoryStream(buffer, false)));
    }

    public static IEmbeddedArchiveSource FromResource(Assembly assembly, string resourceName)
    {
        if (assembly == null)
            throw new ArgumentNullException(nameof(assembly));
        if (resourceName == null)
            throw new ArgumentNullException(nameof(resourceName));

        return new DelegateArchiveSource(async ct =>
        {
            await Task.Yield();
            var stream = assembly.GetManifestResourceStream(resourceName)
                         ?? throw new InvalidOperationException($"Resource '{resourceName}' not found in assembly '{assembly.FullName}'.");

            return await EnsureSeekableAsync(stream, ct).ConfigureAwait(false);
        });
    }

    public static IEmbeddedArchiveSource FromFile(string filePath)
    {
        if (filePath == null)
            throw new ArgumentNullException(nameof(filePath));

        return new DelegateArchiveSource(async ct =>
        {
            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            return await EnsureSeekableAsync(stream, ct).ConfigureAwait(false);
        });
    }

    private static async ValueTask<Stream> EnsureSeekableAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
        {
            if (stream.Position != 0)
                stream.Seek(0, SeekOrigin.Begin);

            return stream;
        }

        var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        stream.Dispose();
        buffer.Seek(0, SeekOrigin.Begin);

        return buffer;
    }

    private sealed class DelegateArchiveSource : IEmbeddedArchiveSource
    {
        private readonly Func<CancellationToken, ValueTask<Stream>> _factory;

        public DelegateArchiveSource(Func<CancellationToken, ValueTask<Stream>> factory)
            => this._factory = factory ?? throw new ArgumentNullException(nameof(factory));

        public async ValueTask<Stream> OpenArchiveAsync(CancellationToken cancellationToken)
        {
            var stream = await this._factory(cancellationToken).ConfigureAwait(false);
            if (!stream.CanSeek)
                stream = await EnsureSeekableAsync(stream, cancellationToken).ConfigureAwait(false);
            else if (stream.Position != 0)
                stream.Seek(0, SeekOrigin.Begin);

            return stream;
        }
    }
}