// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Itexoft.Caching;
using Itexoft.Collections;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Net.Core;
using Itexoft.Threading;
using TtlPolicy = Itexoft.Core.ValuePolicy<System.TimeSpan>;

namespace Itexoft.Net.Dns;

public class NetDnsClient : INetDnsResolver, IDisposable
{
    public enum NetworkPreference
    {
        Auto,
        PreferIpv6,
        PreferIpv4,
        Ipv6Only,
        Ipv4Only,
    }

    private const int dnsHeaderLength = 12;
    private const int maxUdpDnsMessageLength = 1232;
    private const int maxTcpDnsMessageLength = 4096;

    private static readonly IReadOnlyList<DnsRecordType> queryIPv4 = [DnsRecordType.A];
    private static readonly IReadOnlyList<DnsRecordType> queryIPv6 = [DnsRecordType.Aaaa];
    private static readonly IReadOnlyList<DnsRecordType> queryAny = [DnsRecordType.A, DnsRecordType.Aaaa];
    private readonly DeferredCoalescingTtlCache<NetHost, NetIpAddress[]> cache = new();
    private readonly RetryPolicy retryPolicy;
    private readonly NetIpEndpoint[] servers;
    private readonly TtlPolicy ttlPolicy;
    private Disposed disposed;

    public NetDnsClient(params NetIpEndpoint[] servers) : this(TimeSpan.Zero, servers) { }
    public NetDnsClient(TtlPolicy ttlPolicy, params NetIpEndpoint[] servers) : this(ttlPolicy, default, servers) { }
    public NetDnsClient(RetryPolicy retryPolicy, params NetIpEndpoint[] servers) : this(TimeSpan.Zero, retryPolicy, servers) { }

    public NetDnsClient(TtlPolicy ttlPolicy, RetryPolicy retryPolicy, params NetIpEndpoint[] servers)
    {
        if (servers.Required().Length == 0)
            throw new ArgumentException("At least one DNS server endpoint must be provided.", nameof(servers));

        this.ttlPolicy = ttlPolicy;
        this.retryPolicy = retryPolicy;
        this.servers = servers.Copy();
    }

    public NetworkPreference Preference { get; set; } = NetworkPreference.Auto;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        this.cache.Clear();
        GC.SuppressFinalize(this);
    }

    public async ValueTask<NetIpAddress> ResolveAsync(NetHost host, CancelToken cancelToken = default)
    {
        if (host.TryParseIp(out var ip))
            return ip;

        if (host.TryParseIp(out var parsed) && (host.AddressFamily == AddressFamily.Unspecified || parsed.AddressFamily == host.AddressFamily))
            return parsed;

        this.disposed.ThrowIf();
        cancelToken.ThrowIf();
        var result = await this.retryPolicy.WithTimeout(this.Timeout).Run((ct) => this.ResolveAsyncCore(host, ct), cancelToken).ConfigureAwait(false);

        return result[0];
    }

    public ValueTask<NetIpAddress[]> ResolveAllAsync(NetHost host, CancelToken cancelToken = default)
    {
        if (host.TryParseIp(out var ip))
            return ValueTask.FromResult<NetIpAddress[]>([ip]);

        if (host.TryParseIp(out var parsed) && (host.AddressFamily == AddressFamily.Unspecified || parsed.AddressFamily == host.AddressFamily))
            return ValueTask.FromResult<NetIpAddress[]>([parsed]);

        this.disposed.ThrowIf();
        cancelToken.ThrowIf();

        return this.retryPolicy.WithTimeout(this.Timeout).Run((ct) => this.ResolveAsyncCore(host, ct), cancelToken);
    }

    private protected virtual async Task<NetIpAddress[]> ResolveAsyncCore(NetHost host, CancelToken cancelToken)
    {
        this.disposed.ThrowIf();
        cancelToken.ThrowIf();

        var addresses = await this.cache.GetOrAddAsync(
            host,
            async (h, ct) =>
            {
                var result = await this.ResolveCoreAsync(h, ct, 0, true).ConfigureAwait(false);

                if (result is not { Addresses: { Length: > 0 } resolved })
                    throw new SocketException((int)SocketError.HostNotFound);

                var ttl = this.SelectTtl(result.Value.Ttl);

                return new(resolved, ttl);
            },
            cancelToken).ConfigureAwait(false);

        return addresses;
    }

    private IReadOnlyList<DnsRecordType> GetQueryTypes(AddressFamily addressFamily) => addressFamily switch
    {
        AddressFamily.InterNetwork => queryIPv4,
        AddressFamily.InterNetworkV6 => queryIPv6,
        _ => this.Preference switch
        {
            NetworkPreference.Ipv4Only => queryIPv4,
            NetworkPreference.Ipv6Only => queryIPv6,
            NetworkPreference.PreferIpv6 => queryIPv6.Concat(queryIPv4).ToArray(),
            NetworkPreference.PreferIpv4 => queryIPv4.Concat(queryIPv6).ToArray(),
            _ => queryAny,
        },
    };

    private async Task<DnsResult?> ResolveCoreAsync(NetHost host, CancelToken cancelToken, int depth, bool aggregateAll)
    {
        if (depth > 4)
            return null;

        var recordTypes = this.GetQueryTypes(host.AddressFamily);
        var orderedServers = this.SelectServers(await NetStatus.Ipv4AvailableAsync, await NetStatus.Ipv6AvailableAsync);
        SocketException? lastSocketException = null;
        DnsResponse? lastResponse = null;
        List<NetIpAddress>? aggregated = aggregateAll ? [] : null;
        var ttl = TimeSpan.MaxValue;

        foreach (var recordType in recordTypes)
        {
            foreach (var server in orderedServers)
            {
                this.disposed.ThrowIf();
                cancelToken.ThrowIf();

                try
                {
                    var response = await this.QueryServerAsync(server, host.Host, recordType, cancelToken).ConfigureAwait(false);
                    lastResponse = response;

                    switch (response.Status)
                    {
                        case DnsResponseStatus.Success when response.Addresses.Length > 0:
                            if (!aggregateAll)
                                return new(response.Addresses, response.Ttl);

                            aggregated ??= [];
                            aggregated.AddRange(response.Addresses);
                            ttl = response.Ttl < ttl ? response.Ttl : ttl;

                            goto NextRecordType;
                        case DnsResponseStatus.Success when response.CanonicalName is { } cname:
                        {
                            var cnameKey = new NetHost(cname, host.AddressFamily);

                            return await this.ResolveCoreAsync(cnameKey, cancelToken, depth + 1, aggregateAll).ConfigureAwait(false);
                        }
                        case DnsResponseStatus.NameError:
                            return null;
                        case DnsResponseStatus.Truncated:
                            break;
                        case DnsResponseStatus.TemporaryFailure:
                        case DnsResponseStatus.Empty:
                        default:
                            break;
                    }
                }
                catch (SocketException ex)
                {
                    lastSocketException = ex;
                }
            }

            NextRecordType: ;
        }

        if (lastResponse is { Status: DnsResponseStatus.NameError })
            return null;

        if (aggregated is not null && aggregated.Count > 0)
            return new(aggregated.ToArray(), ttl < TimeSpan.MaxValue ? ttl : TimeSpan.Zero);

        if (lastResponse is { Status: DnsResponseStatus.Empty } && lastSocketException is null)
            return null;

        if (lastResponse is { Status: DnsResponseStatus.TemporaryFailure })
            throw new SocketException((int)SocketError.TryAgain);

        if (lastSocketException is not null)
            throw lastSocketException.Rethrow();

        this.disposed.ThrowIf();
        cancelToken.ThrowIf();

        return null;
    }

    private async Task<DnsResponse> QueryServerAsync(NetIpEndpoint server, string host, DnsRecordType recordType, CancelToken cancelToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(maxUdpDnsMessageLength);

        try
        {
            this.disposed.ThrowIf();
            cancelToken.ThrowIf();
            var queryLength = WriteQuery(host, recordType, buffer, out var messageId);

            var udpResponse = await this.QueryUdpAsync(server, buffer, queryLength, messageId, recordType, cancelToken).ConfigureAwait(false);

            if (udpResponse.Status != DnsResponseStatus.Truncated && udpResponse.Status != DnsResponseStatus.None)
                return udpResponse;

            queryLength = WriteQuery(host, recordType, buffer, out messageId);

            return await this.QueryTcpAsync(server, buffer, queryLength, messageId, recordType, cancelToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task<DnsResponse> QueryUdpAsync(
        NetIpEndpoint server,
        byte[] buffer,
        int queryLength,
        ushort messageId,
        DnsRecordType recordType,
        CancelToken cancelToken)
    {
        this.disposed.ThrowIf();
        cancelToken.ThrowIf();
        await using var socket = new NetSocket(server.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        socket.SendTimeout = socket.ReceiveTimeout = this.Timeout;
        await socket.ConnectAsync(server, cancelToken).ConfigureAwait(false);
        await socket.SendAsync(new(buffer, 0, queryLength), SocketFlags.None, cancelToken).ConfigureAwait(false);
        var received = await socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, cancelToken).ConfigureAwait(false);

        if (received <= 0)
            return DnsResponse.None;

        return ParseResponse(buffer.AsSpan(0, received), messageId, recordType);
    }

    private async Task<DnsResponse> QueryTcpAsync(
        NetIpEndpoint server,
        byte[] buffer,
        int queryLength,
        ushort messageId,
        DnsRecordType recordType,
        CancelToken cancelToken)
    {
        this.disposed.ThrowIf();
        cancelToken.ThrowIf();
        await using var socket = new NetSocket(server.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        socket.SendTimeout = socket.ReceiveTimeout = this.Timeout;
        await socket.ConnectAsync(server, cancelToken).ConfigureAwait(false);

        var lengthPrefix = (ushort)queryLength;
        var prefix = ArrayPool<byte>.Shared.Rent(2);

        try
        {
            BinaryPrimitives.WriteUInt16BigEndian(prefix, lengthPrefix);

            if (!await sendAllAsync(prefix, 0, 2).ConfigureAwait(false))
                return DnsResponse.None;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(prefix);
        }

        if (!await sendAllAsync(buffer, 0, queryLength).ConfigureAwait(false))
            return DnsResponse.None;

        var receiveBuffer = ArrayPool<byte>.Shared.Rent(maxTcpDnsMessageLength);

        try
        {
            if (!await receiveAllAsync(receiveBuffer, 0, 2).ConfigureAwait(false))
                return DnsResponse.None;

            var responseLength = BinaryPrimitives.ReadUInt16BigEndian(receiveBuffer);

            if (responseLength is <= 0 or > maxTcpDnsMessageLength)
                return DnsResponse.None;

            if (!await receiveAllAsync(receiveBuffer, 0, responseLength).ConfigureAwait(false))
                return DnsResponse.None;

            return ParseResponse(receiveBuffer.AsSpan(0, responseLength), messageId, recordType);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(receiveBuffer);
        }

        async Task<bool> sendAllAsync(byte[] data, int offset, int count)
        {
            var sent = 0;

            while (sent < count)
            {
                var written = await socket.SendAsync(new(data, offset + sent, count - sent), SocketFlags.None, cancelToken).ConfigureAwait(false);

                if (written <= 0)
                    return false;

                sent += written;
            }

            return true;
        }

        async Task<bool> receiveAllAsync(byte[] data, int offset, int count)
        {
            var readTotal = 0;

            while (readTotal < count)
            {
                var read = await socket.ReceiveAsync(data.AsMemory(offset + readTotal, count - readTotal), SocketFlags.None, cancelToken)
                    .ConfigureAwait(false);

                if (read == 0)
                    return false;

                readTotal += read;
            }

            return true;
        }
    }

    private static int WriteQuery(string host, DnsRecordType recordType, byte[] buffer, out ushort messageId)
    {
        if (buffer.Length < dnsHeaderLength + 4)
            throw new ArgumentException("DNS buffer is too small.", nameof(buffer));

        var span = buffer.AsSpan();
        messageId = (ushort)RandomNumberGenerator.GetInt32(0, ushort.MaxValue + 1);

        BinaryPrimitives.WriteUInt16BigEndian(span, messageId);
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], 0x0100); // RD=1
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], 1); // QDCOUNT
        BinaryPrimitives.WriteUInt16BigEndian(span[6..], 0); // ANCOUNT
        BinaryPrimitives.WriteUInt16BigEndian(span[8..], 0); // NSCOUNT
        BinaryPrimitives.WriteUInt16BigEndian(span[10..], 0); // ARCOUNT

        var offset = dnsHeaderLength;

        if (!TryWriteName(host, span, ref offset))
            throw new FormatException("Host name is invalid for DNS wire format.");

        if (offset + 4 > span.Length)
            throw new ArgumentException("DNS buffer is too small for question.", nameof(buffer));

        BinaryPrimitives.WriteUInt16BigEndian(span[offset..], (ushort)recordType);
        offset += 2;
        BinaryPrimitives.WriteUInt16BigEndian(span[offset..], 1); // IN class
        offset += 2;

        return offset;
    }

    private static bool TryWriteName(string host, Span<byte> buffer, ref int offset)
    {
        var span = host.AsSpan();
        var start = 0;

        if (span.Length == 0)
            return false;

        while (start < span.Length)
        {
            var dotIndex = span[start..].IndexOf('.');
            var length = dotIndex >= 0 ? dotIndex : span.Length - start;

            if (length is 0 or > 63)
                return false;

            var label = span.Slice(start, length);
            var byteCount = Encoding.ASCII.GetByteCount(label);

            if (offset + 1 + byteCount + 4 > buffer.Length) // include space for terminator and qtype/qclass guard
                return false;

            buffer[offset++] = (byte)byteCount;
            var written = Encoding.ASCII.GetBytes(label, buffer[offset..]);
            offset += written;

            start += length + 1;

            if (dotIndex < 0)
                break;
        }

        if (offset >= buffer.Length)
            return false;

        buffer[offset++] = 0;

        return true;
    }

    private static DnsResponse ParseResponse(ReadOnlySpan<byte> response, ushort messageId, DnsRecordType expectedType)
    {
        if (response.Length < dnsHeaderLength)
            return DnsResponse.None;

        if (BinaryPrimitives.ReadUInt16BigEndian(response) != messageId)
            return DnsResponse.None;

        var flags = BinaryPrimitives.ReadUInt16BigEndian(response[2..]);
        var isResponse = (flags & 0x8000) != 0;
        var truncated = (flags & 0x0200) != 0;
        var rcode = (DnsRCode)(flags & 0x000F);

        if (!isResponse)
            return DnsResponse.None;

        if (truncated)
            return DnsResponse.Truncated;

        switch (rcode)
        {
            case DnsRCode.NxDomain:
                return DnsResponse.NameError;
            case DnsRCode.ServFail or DnsRCode.Refused:
                return DnsResponse.TemporaryFailure;
        }

        var qdCount = BinaryPrimitives.ReadUInt16BigEndian(response[4..]);
        var anCount = BinaryPrimitives.ReadUInt16BigEndian(response[6..]);

        var offset = dnsHeaderLength;
        var answers = new List<NetIpAddress>();
        string? cname = null;
        TimeSpan? ttlMin = null;

        for (var i = 0; i < qdCount; i++)
        {
            if (!TrySkipName(response, ref offset))
                return DnsResponse.None;

            offset += 4;

            if (offset > response.Length)
                return DnsResponse.None;
        }

        for (var i = 0; i < anCount; i++)
        {
            if (!TrySkipName(response, ref offset))
                return DnsResponse.None;

            if (offset + 10 > response.Length)
                return DnsResponse.None;

            var type = (DnsRecordType)BinaryPrimitives.ReadUInt16BigEndian(response[offset..]);
            var recordClass = BinaryPrimitives.ReadUInt16BigEndian(response[(offset + 2)..]);
            var ttlSeconds = BinaryPrimitives.ReadUInt32BigEndian(response[(offset + 4)..]);
            var rdLength = BinaryPrimitives.ReadUInt16BigEndian(response[(offset + 8)..]);
            offset += 10;

            if (offset + rdLength > response.Length)
                return DnsResponse.None;

            if (recordClass == 1)
            {
                if (type == expectedType)
                {
                    var address = ParseAddress(type, response.Slice(offset, rdLength));

                    if (!address.IsAny)
                    {
                        var ttl = ToTtl(ToTimeSpanSeconds(ttlSeconds));
                        ttlMin = ttlMin is null ? ttl : TimeSpan.FromTicks(Math.Min(ttlMin.Value.Ticks, ttl.Ticks));
                        answers.Add(address);
                    }
                }
                else if (type == DnsRecordType.Cname && cname is null)
                {
                    var name = ReadName(response, offset, offset + rdLength);

                    if (name is not null)
                        cname = name;
                }
            }

            offset += rdLength;
        }

        if (answers.Count > 0)
            return new(DnsResponseStatus.Success, [..answers], ttlMin ?? TimeSpan.Zero, cname);

        if (cname is not null)
            return new(DnsResponseStatus.Success, [], TimeSpan.Zero, cname);

        return DnsResponse.Empty;
    }

    private static NetIpAddress ParseAddress(DnsRecordType type, ReadOnlySpan<byte> data) => type switch
    {
        DnsRecordType.A when data.Length == 4 => new(data),
        DnsRecordType.Aaaa when data.Length == 16 => new(data),
        _ => default,
    };

    private static bool TrySkipName(ReadOnlySpan<byte> buffer, ref int offset)
    {
        const int pointerMask = 0xC0;
        const int pointerLength = 2;
        var jumps = 0;

        while (true)
        {
            if (offset >= buffer.Length)
                return false;

            var length = buffer[offset];

            if ((length & pointerMask) == pointerMask)
            {
                if (offset + 1 >= buffer.Length)
                    return false;

                offset += pointerLength;

                return true;
            }

            if (length == 0)
            {
                offset++;

                return true;
            }

            offset += 1 + length;

            if (offset > buffer.Length)
                return false;

            if (++jumps > byte.MaxValue)
                return false;
        }
    }

    private static string? ReadName(ReadOnlySpan<byte> message, int startOffset, int maxOffset)
    {
        const int pointerMask = 0xC0;
        Span<char> labelBuffer = stackalloc char[63];
        var labels = new List<string>();
        var offset = startOffset;
        var jumps = 0;

        while (true)
        {
            if (offset >= message.Length || offset >= maxOffset)
                return null;

            var length = message[offset];

            if ((length & pointerMask) == pointerMask)
            {
                if (offset + 1 >= message.Length)
                    return null;

                offset = ((length & ~pointerMask) << 8) | message[offset + 1];

                if (++jumps > byte.MaxValue)
                    return null;

                continue;
            }

            if (length == 0)
                break;

            offset++;

            if (length > labelBuffer.Length || offset + length > message.Length)
                return null;

            for (var i = 0; i < length; i++)
                labelBuffer[i] = (char)message[offset + i];

            labels.Add(new(labelBuffer[..length]));
            offset += length;
        }

        return labels.Count == 0 ? string.Empty : string.Join('.', labels);
    }

    private List<NetIpEndpoint> SelectServers(bool ipv4Available, bool ipv6Available)
    {
        var span = this.servers.AsSpan();
        var ordered = new List<NetIpEndpoint>(span.Length);
        var added = span.Length > 0 ? new bool[span.Length] : Array.Empty<bool>();

        if (this.Preference == NetworkPreference.Auto)
        {
            AddAvailableInOrder(span, ipv4Available, ipv6Available, ordered, added);
            AddRemainingInOrder(span, ordered, added);

            return ordered;
        }

        AddressFamily primary;
        AddressFamily secondary;
        var allowSecondary = true;

        switch (this.Preference)
        {
            case NetworkPreference.PreferIpv6:
                primary = AddressFamily.InterNetworkV6;
                secondary = AddressFamily.InterNetwork;

                break;
            case NetworkPreference.PreferIpv4:
                primary = AddressFamily.InterNetwork;
                secondary = AddressFamily.InterNetworkV6;

                break;
            case NetworkPreference.Ipv6Only:
                primary = AddressFamily.InterNetworkV6;
                secondary = AddressFamily.Unspecified;
                allowSecondary = false;

                break;
            case NetworkPreference.Ipv4Only:
                primary = AddressFamily.InterNetwork;
                secondary = AddressFamily.Unspecified;
                allowSecondary = false;

                break;
            case NetworkPreference.Auto:
            default:
                primary = AddressFamily.InterNetwork;
                secondary = AddressFamily.InterNetworkV6;

                break;
        }

        AddFamily(span, primary, ordered, added, ipv4Available, ipv6Available, true);

        if (allowSecondary)
            AddFamily(span, secondary, ordered, added, ipv4Available, ipv6Available, true);

        AddFamily(span, primary, ordered, added, ipv4Available, ipv6Available, false);

        if (allowSecondary)
            AddFamily(span, secondary, ordered, added, ipv4Available, ipv6Available, false);

        if (ordered.Count == 0)
            AddRemainingInOrder(span, ordered, added);

        return ordered;
    }

    private static void AddAvailableInOrder(
        ReadOnlySpan<NetIpEndpoint> servers,
        bool ipv4Available,
        bool ipv6Available,
        List<NetIpEndpoint> ordered,
        bool[] added)
    {
        for (var i = 0; i < servers.Length; i++)
        {
            if (added[i])
                continue;

            ref readonly var server = ref servers[i];

            if (!IsAvailable(server.AddressFamily, ipv4Available, ipv6Available))
                continue;

            ordered.Add(server);
            added[i] = true;
        }
    }

    private static void AddRemainingInOrder(ReadOnlySpan<NetIpEndpoint> servers, List<NetIpEndpoint> ordered, bool[] added)
    {
        for (var i = 0; i < servers.Length; i++)
        {
            if (added[i])
                continue;

            ordered.Add(servers[i]);
            added[i] = true;
        }
    }

    private static void AddFamily(
        ReadOnlySpan<NetIpEndpoint> servers,
        AddressFamily addressFamily,
        List<NetIpEndpoint> ordered,
        bool[] added,
        bool ipv4Available,
        bool ipv6Available,
        bool requireAvailable)
    {
        if (addressFamily == AddressFamily.Unspecified)
            return;

        var available = IsAvailable(addressFamily, ipv4Available, ipv6Available);

        if (requireAvailable && !available)
            return;

        for (var i = 0; i < servers.Length; i++)
        {
            ref readonly var server = ref servers[i];

            if (added[i] || server.AddressFamily != addressFamily)
                continue;

            ordered.Add(server);
            added[i] = true;
        }
    }

    private static bool IsAvailable(AddressFamily addressFamily, bool ipv4Available, bool ipv6Available) =>
        addressFamily switch
        {
            AddressFamily.InterNetwork => ipv4Available,
            AddressFamily.InterNetworkV6 => ipv6Available,
            _ => false,
        };

    private TimeSpan SelectTtl(TimeSpan dnsTtl) => ToTtl(this.ttlPolicy.Apply(ToTtl(dnsTtl)));

    private static TimeSpan ToTimeSpanSeconds(uint seconds)
    {
        if (seconds == 0)
            return TimeSpan.Zero;

        const uint maxSeconds = int.MaxValue;

        return seconds >= maxSeconds ? TimeSpan.FromSeconds(maxSeconds) : TimeSpan.FromSeconds(seconds);
    }

    private static TimeSpan ToTtl(TimeSpan ttl) => ttl >= TimeSpan.Zero ? ttl : TimeSpan.Zero;

    private readonly record struct DnsResult(NetIpAddress[] Addresses, TimeSpan Ttl);

    private readonly record struct DnsResponse(DnsResponseStatus Status, NetIpAddress[] Addresses, TimeSpan Ttl, string? CanonicalName)
    {
        public static DnsResponse None { get; } = new(DnsResponseStatus.None, [], TimeSpan.Zero, null);
        public static DnsResponse Empty { get; } = new(DnsResponseStatus.Empty, [], TimeSpan.Zero, null);
        public static DnsResponse NameError { get; } = new(DnsResponseStatus.NameError, [], TimeSpan.Zero, null);
        public static DnsResponse Truncated { get; } = new(DnsResponseStatus.Truncated, [], TimeSpan.Zero, null);
        public static DnsResponse TemporaryFailure { get; } = new(DnsResponseStatus.TemporaryFailure, [], TimeSpan.Zero, null);
    }

    private enum DnsRecordType : ushort
    {
        A = 1,
        Aaaa = 28,
        Cname = 5,
    }

    private enum DnsResponseStatus : byte
    {
        None,
        Success,
        Empty,
        NameError,
        TemporaryFailure,
        Truncated,
    }

    private enum DnsRCode : ushort
    {
        NoError = 0,
        FormErr = 1,
        ServFail = 2,
        NxDomain = 3,
        NotImp = 4,
        Refused = 5,
    }
}
