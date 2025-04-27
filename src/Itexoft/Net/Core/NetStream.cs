// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net.Sockets;
using Itexoft.Extensions;
using Itexoft.IO;
using Itexoft.Threading;

namespace Itexoft.Net.Core;

public interface INetStream : IStreamRwta;

public class NetStream : StreamBase, INetStream
{
    private readonly NetSocket socket;
    private readonly NetworkStream stream;

    public NetStream(NetSocket socket, bool ownsSocket) : this(socket, FileAccess.ReadWrite, ownsSocket) { }
    public NetStream(NetSocket socket, FileAccess access) : this(socket, access, true) { }

    public NetStream(NetSocket socket, FileAccess access, bool ownsSocket) : this(socket, new NetworkStream(socket.socket, access, ownsSocket)) { }

    private NetStream(NetSocket socket, NetworkStream stream)
    {
        this.socket = socket.Required();
        this.stream = stream.Required();
    }

    public TimeSpan WriteTimeout
    {
        get => TimeSpan.FromMilliseconds(this.stream.WriteTimeout);
        set => this.stream.WriteTimeout = value.TimeoutMilliseconds;
    }

    public TimeSpan ReadTimeout
    {
        get => TimeSpan.FromMilliseconds(this.stream.ReadTimeout);
        set => this.stream.ReadTimeout = value.TimeoutMilliseconds;
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancelToken cancelToken = default)
    {
        using (cancelToken.Bridge(out var token))
            return await this.stream.ReadAsync(buffer, token);
    }

    public async ValueTask FlushAsync(CancelToken cancelToken)
    {
        using (cancelToken.Bridge(out var token))
            await this.stream.FlushAsync(token);
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancelToken cancelToken = default)
    {
        using (cancelToken.Bridge(out var token))
            await this.stream.WriteAsync(buffer, token);
    }

    protected override ValueTask DisposeAny()
    {
        this.stream.Dispose();

        return ValueTask.CompletedTask;
    }

    public static NetStream Wrap(NetStream stream)
    {
        var newStream = new NetStream(stream.socket, stream.stream);

        return newStream;
    }
}
