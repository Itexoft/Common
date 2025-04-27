// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Itexoft.Extensions;

namespace Itexoft.Core;

public readonly struct ValuePolicy
{
    private readonly PolicyKind kind;
    private readonly ValueBuffer buffer;
    private readonly object? reference;

    private ValuePolicy(PolicyKind kind, ValueBuffer buffer, object? reference)
    {
        this.kind = kind;
        this.buffer = buffer;
        this.reference = reference;
    }

    public static ValuePolicy Default { get; } = new(PolicyKind.Default, default, null);

    public static ValuePolicy PassThrough { get; } = new(PolicyKind.PassThrough, default, null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValuePolicy Constant<T>(T? value)
    {
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>() && Unsafe.SizeOf<T>() <= ValueBuffer.Size)
        {
            var buffer = default(ValueBuffer);
            Unsafe.WriteUnaligned(ref Unsafe.As<ValueBuffer, byte>(ref buffer), value);

            return new(PolicyKind.Value, buffer, null);
        }

        return new(PolicyKind.Reference, default, value!);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValuePolicy Delegate<TValue>(Func<TValue?, TValue?> selector) => new(PolicyKind.Delegate, default, selector.Required());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Func<TValue?, TValue?> ApplyDefault<TValue>() => Default.Apply;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Func<TValue?, TValue?> ApplyPassThrough<TValue>() => PassThrough.Apply;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Func<TValue?, TValue?> ApplyConstant<TValue>(TValue? value) => Constant(value).Apply;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Func<TValue?, TValue?> ApplyDelegate<TValue>(Func<TValue?, TValue?> value) => Delegate(value).Apply;

    public TValue? Apply<TValue>(TValue? value) => this.kind switch
    {
        PolicyKind.PassThrough => value,
        PolicyKind.Reference => (TValue?)this.reference,
        PolicyKind.Value => Unsafe.ReadUnaligned<TValue>(ref Unsafe.As<ValueBuffer, byte>(ref Unsafe.AsRef(in this.buffer))),
        PolicyKind.Delegate => ((Func<TValue?, TValue?>)this.reference!)(value),
        PolicyKind.Default => default,
        _ => throw new InvalidOperationException(nameof(this.kind)),
    };

    private enum PolicyKind : byte
    {
        Default,
        PassThrough,
        Value,
        Reference,
        Delegate,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ValueBuffer
    {
        public const int Size = sizeof(long) * 2;
        public long A;
        public long B;
    }
}

public readonly struct ValuePolicy<TValue>
{
    private readonly ValuePolicy valuePolicy;

    public ValuePolicy(Func<TValue?, TValue?> valuePolicy) => this.valuePolicy = ValuePolicy.Delegate(valuePolicy);

    public ValuePolicy(ValuePolicy valuePolicy) => this.valuePolicy = valuePolicy;

    public static Func<TValue?, TValue?> ApplyDefault { get; } = ValuePolicy.ApplyDefault<TValue>();
    public static Func<TValue?, TValue?> ApplyPassThrough { get; } = ValuePolicy.ApplyPassThrough<TValue>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Func<TValue?, TValue?> ApplyConstant(TValue? value) => ValuePolicy.ApplyConstant(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Func<TValue?, TValue?> ApplyDelegate(Func<TValue?, TValue?> value) => ValuePolicy.ApplyDelegate(value);

    public TValue? Apply(TValue? value) => this.valuePolicy.Apply(value);

    public static implicit operator ValuePolicy<TValue>(ValuePolicy valuePolicy) => new(valuePolicy);
    public static implicit operator ValuePolicy<TValue>(Func<TValue?, TValue?> selector) => new(selector);
    public static implicit operator ValuePolicy(ValuePolicy<TValue> valuePolicy) => valuePolicy.valuePolicy;
    public static implicit operator ValuePolicy<TValue>(TValue value) => ValuePolicy.Constant(value);
}
