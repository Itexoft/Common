// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Itexoft.Threading.Core.Lane;

public sealed class InlineLaneCombiner64<TState, TRequest, TResult, TOp>(TState state) where TOp : struct, ILaneOp64<TState, TRequest, TResult>
{
    private const int combinerFree = 0;
    private const int combinerBusy = 1;
    private int combiner;
    private ulong completedMask;

    private ulong pendingMask;

    private Inline64<TRequest> requests;
    private Inline64<TResult> results;

    private TState state = state;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TState DangerousGetStateRef() => ref this.state;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TResult Invoke(in Lane64 lane, in TRequest request)
    {
        if ((uint)lane.Index > 63u)
            throw new ArgumentOutOfRangeException(nameof(lane));

        if (!this.TryPublish(in lane, in request))
            throw new InvalidOperationException("Lane has an in-flight request.");

        this.HelpOrWait(in lane);

        return this.Consume(in lane);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryInvoke(in Lane64 lane, in TRequest request, out TResult result)
    {
        if ((uint)lane.Index > 63u)
            throw new ArgumentOutOfRangeException(nameof(lane));

        if (!this.TryPublish(in lane, in request))
        {
            result = default!;

            return false;
        }

        this.HelpOrWait(in lane);

        result = this.Consume(in lane);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryDrain()
    {
        if (!this.TryEnterCombiner())
            return false;

        try
        {
            this.DrainAll();

            return true;
        }
        finally
        {
            this.ExitCombiner();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryPublish(in Lane64 lane, in TRequest request)
    {
        var bit = lane.Bit;

        if (((Volatile.Read(ref this.pendingMask) | Volatile.Read(ref this.completedMask)) & bit) != 0)
            return false;

        Inline64<TRequest>.GetUnchecked(ref this.requests, lane.Index) = request;
        Interlocked.Or(ref this.pendingMask, bit);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TResult Consume(in Lane64 lane)
    {
        var bit = lane.Bit;

        ref var slot = ref Inline64<TResult>.GetUnchecked(ref this.results, lane.Index);
        var result = slot;

        slot = default!;
        Interlocked.And(ref this.completedMask, ~bit);

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void HelpOrWait(in Lane64 lane)
    {
        if (this.TryEnterCombiner())
        {
            try
            {
                this.DrainUntilCompleted(in lane);
                this.DrainAll();
            }
            finally
            {
                this.ExitCombiner();
            }

            return;
        }

        this.WaitCompletedOrHelp(in lane);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WaitCompletedOrHelp(in Lane64 lane)
    {
        var bit = lane.Bit;
        var sw = new SpinWait();

        while ((Volatile.Read(ref this.completedMask) & bit) == 0)
        {
            if (Volatile.Read(ref this.combiner) == combinerFree && this.TryEnterCombiner())
            {
                try
                {
                    this.DrainUntilCompleted(in lane);
                    this.DrainAll();
                }
                finally
                {
                    this.ExitCombiner();
                }

                return;
            }

            sw.SpinOnce();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryEnterCombiner() => Interlocked.CompareExchange(ref this.combiner, combinerBusy, combinerFree) == combinerFree;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExitCombiner() => Volatile.Write(ref this.combiner, combinerFree);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DrainUntilCompleted(in Lane64 lane)
    {
        var bit = lane.Bit;
        var sw = new SpinWait();

        while ((Volatile.Read(ref this.completedMask) & bit) == 0)
        {
            if (!this.DrainBatch())
                sw.SpinOnce();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DrainAll()
    {
        while (this.DrainBatch()) { }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool DrainBatch()
    {
        var batch = Interlocked.Exchange(ref this.pendingMask, 0);

        if (batch == 0)
            return false;

        while (batch != 0)
        {
            var index = BitOperations.TrailingZeroCount(batch);
            var bit = 1UL << index;
            batch &= batch - 1;

            ref var requestSlot = ref Inline64<TRequest>.GetUnchecked(ref this.requests, index);

            TOp.Invoke(ref this.state, in requestSlot, out var result);

            requestSlot = default!;

            Inline64<TResult>.GetUnchecked(ref this.results, index) = result;
            Interlocked.Or(ref this.completedMask, bit);
        }

        return true;
    }
}
