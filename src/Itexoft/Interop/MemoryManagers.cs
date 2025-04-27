// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Itexoft.Interop;

public static unsafe class MemoryManagerExtensions
{
    extension<T>(ReadOnlySpan<T> buffer) where T : unmanaged
    {
        [Experimental(
            nameof(AsMemory),
            Message =
                "Dangerous Span-to-Memory bridge with no ownership. The returned ReadOnlyMemory is valid only while the source span remains valid; do not store it.")]
        public ReadOnlyMemory<T> AsMemory()
        {
            fixed (T* pointer = &buffer.GetPinnableReference())
            {
                using var memoryManager = new MemoryConverter<T>(pointer, buffer.Length); // allocation

                return memoryManager.Memory;
            }
        }
    }

    private sealed class MemoryConverter<T>(T* pointer, int length) : MemoryManager<T> where T : unmanaged
    {
        public override Span<T> GetSpan() => new(pointer, length);

        public override MemoryHandle Pin(int elementIndex = 0) => new(pointer + elementIndex);

        public override void Unpin() { }

        protected override void Dispose(bool disposing) { }
    }
}

public unsafe sealed class MemoryManagerBase<T>(T* pointer, int length) : MemoryManager<T> where T : unmanaged
{
    public override Span<T> GetSpan() => new(pointer, length);

    public override MemoryHandle Pin(int elementIndex = 0)
    {
        if ((uint)elementIndex > (uint)length)
            throw new ArgumentOutOfRangeException(nameof(elementIndex));

        return new(pointer + elementIndex);
    }

    public override void Unpin() { }

    protected override void Dispose(bool disposing) { }
}
