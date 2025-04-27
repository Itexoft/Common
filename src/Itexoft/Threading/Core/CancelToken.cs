// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Reflection;

namespace Itexoft.Threading;

public struct CancelToken : IEquatable<CancelToken>
{
    private static readonly Func<CancellationToken, CancellationTokenSource?> getCancellationTokenSource =
        FieldExtractor.BuildGetter<CancellationToken, CancellationTokenSource>();

    private static readonly ConditionalWeakTable<object, StrongBox<State>> states = new();

    private StrongBox<State>? state;
    private CancellationTokenSource? cts;

    private struct State
    {
        public StrongBox<State>? Parent;
        public long Deadline;
        public CancellationTokenSource? BridgeCts;
        public int Requested;
    }

    public CancelToken() => this.state = null;

    public CancelToken(CancellationToken cancellationToken) : this(getCancellationTokenSource(cancellationToken).Required())
    {
#pragma warning disable CS8974 // Converting method group to non-delegate type
        cancellationToken.UnsafeRegister(static c => ((Func<bool>)c!).Invoke(), this.Cancel);
#pragma warning restore CS8974 // Converting method group to non-delegate type
    }

    public CancelToken(object source) => this.state = states.GetValue(source.Required(), static _ => new StrongBox<State>(new State()));

    public static CancelToken None { get; } = default;

    public bool IsNone
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.state is null;
    }

    public bool IsRequested
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.state is not null && IsRequestedInternal(this.state);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Cancel()
    {
        var state = this.state;

        if (state is null)
            return false;

        if (IsRequestedInternal(state))
            return false;

        var canceled = Request(state);

        if (canceled)
            this.cts = null;

        return canceled;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CancelToken ThrowIf()
    {
        if (this.IsRequested)
            throw new OperationCanceledException();

        return this;
    }

    public CancelToken Branch(TimeSpan timeout = default)
    {
        var parent = this.state;
        var deadline = CreateDeadline(timeout);

        var parentRequested = parent is not null && IsRequestedInternal(parent);

        var cancelToken = new CancelToken();

        cancelToken.state = new StrongBox<State>(
            new State
            {
                Parent = parent,
                Deadline = deadline,
                Requested = parentRequested ? 1 : 0,
            });

        return cancelToken;
    }

    public bool Equals(CancelToken other) => ReferenceEquals(this.state, other.state);

    public override bool Equals(object? obj) => obj is CancelToken other && this.Equals(other);

    public override int GetHashCode() => this.state is null ? 0 : RuntimeHelpers.GetHashCode(this.state);

    public static bool operator ==(CancelToken left, CancelToken right) => left.Equals(right);

    public static bool operator !=(CancelToken left, CancelToken right) => !left.Equals(right);

    public static implicit operator CancelToken(CancellationToken cancellationToken) => new(cancellationToken);

    public IDisposable Bridge(out CancellationToken cancellationToken)
    {
        var state = this.state;

        if (state is null)
        {
            cancellationToken = CancellationToken.None;

            return default(CancellationTokenRegistration);
        }

        this.ThrowIf();

        while (true)
        {
            var cts = GetOrCreateCts(state);

            try
            {
                var token = cts.Token;
                token.UnsafeRegister(CancelCallback, state);
                ScheduleCancelAfter(state, cts);

                cancellationToken = token;
                this.cts = cts;

                return cts;
            }
            catch (ObjectDisposedException)
            {
                ClearCts(state, cts);
                this.cts = null;
                this.ThrowIf();
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CancelCallback(object? state) => Request((StrongBox<State>)state!);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsRequestedInternal(StrongBox<State> state)
    {
        ref var s = ref state.Value;

        if (Volatile.Read(ref s.Requested) != 0)
            return true;

        var deadline = s.Deadline;

        if (deadline != 0 && TimeUtils.CachedTimestampMs - deadline >= 0)
            return Request(state);

        var parent = s.Parent;

        if (parent is not null && IsRequestedInternal(parent))
            return Request(state);

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Request(StrongBox<State> state)
    {
        ref var s = ref state.Value;

        if (Interlocked.Exchange(ref s.Requested, 1) != 0)
            return false;

        var cts = Interlocked.Exchange(ref s.BridgeCts, null);

        if (cts is null)
            return true;

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException) { }

        cts.Dispose();

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long CreateDeadline(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            return 0;

        var ms = timeout.Ticks / TimeSpan.TicksPerMillisecond;

        if (ms <= 0)
            ms = 1;

        var now = TimeUtils.CachedTimestampMs;
        var deadline = now + ms;

        if (deadline < now)
            return long.MaxValue;

        return deadline;
    }

    private static void ScheduleCancelAfter(StrongBox<State> state, CancellationTokenSource cts)
    {
        var deadline = state.Value.Deadline;

        if (deadline == 0)
            return;

        var remaining = deadline - TimeUtils.CachedTimestampMs;

        if (remaining <= 0)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException) { }

            return;
        }

        if (remaining > int.MaxValue)
            return;

        try
        {
            cts.CancelAfter((int)remaining);
        }
        catch (ObjectDisposedException) { }
        catch (ArgumentOutOfRangeException) { }
    }

    private static CancellationTokenSource GetOrCreateCts(StrongBox<State> state)
    {
        while (true)
        {
            ref var s = ref state.Value;
            var existing = Volatile.Read(ref s.BridgeCts);

            if (existing is not null)
            {
                try
                {
                    _ = existing.Token;

                    return existing;
                }
                catch (ObjectDisposedException)
                {
                    ClearCts(state, existing);
                }
            }

            CancellationTokenSource created;
            var parent = s.Parent;

            if (parent is null)
                created = new CancellationTokenSource();
            else
            {
                var parentCts = GetOrCreateCts(parent);
                CancellationToken parentToken;

                try
                {
                    parentToken = parentCts.Token;
                }
                catch (ObjectDisposedException)
                {
                    ClearCts(parent, parentCts);

                    continue;
                }

                created = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
            }

            existing = Interlocked.CompareExchange(ref s.BridgeCts, created, null);

            if (existing is not null)
            {
                created.Dispose();

                continue;
            }

            ScheduleCancelAfter(state, created);

            return created;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ClearCts(StrongBox<State> state, CancellationTokenSource cts)
    {
        ref var s = ref state.Value;
        Interlocked.CompareExchange(ref s.BridgeCts, null, cts);
    }

    public ValueTask Delay(int fromMilliseconds) => this.Delay(TimeSpan.FromMilliseconds(fromMilliseconds));

    public async ValueTask Delay(TimeSpan fromMilliseconds)
    {
        if (fromMilliseconds < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(fromMilliseconds));

        if (fromMilliseconds == TimeSpan.Zero)
            return;

        this.ThrowIf();

        var ticks = fromMilliseconds.Ticks;
        long delayMs;

        if (ticks >= long.MaxValue - (TimeSpan.TicksPerMillisecond - 1))
            delayMs = long.MaxValue / TimeSpan.TicksPerMillisecond;
        else
            delayMs = (ticks + (TimeSpan.TicksPerMillisecond - 1)) / TimeSpan.TicksPerMillisecond;

        if (delayMs <= 0)
            return;

        var startTime = TimeUtils.CachedTimestampMs;

        while (TimeUtils.CachedTimestampMs - startTime < delayMs)
        {
            this.ThrowIf();
            await Task.Yield();
        }
    }
}
