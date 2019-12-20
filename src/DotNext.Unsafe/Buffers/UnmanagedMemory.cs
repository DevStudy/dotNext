﻿using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DotNext.Buffers
{
    using Runtime;

    internal class UnmanagedMemory<T> : MemoryManager<T>
        where T : unmanaged
    {
        private protected IntPtr address;
        private readonly bool owner;

        internal UnmanagedMemory(IntPtr address, int length)
        {
            this.address = address;
            Length = length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private protected unsafe static long SizeOf(int length) => Math.BigMul(length, sizeof(T));

        private protected unsafe UnmanagedMemory(int length, bool zeroMem)
        {
            var size = SizeOf(length);
            address = Marshal.AllocHGlobal(new IntPtr(size));
            GC.AddMemoryPressure(size);
            Length = length;
            if (zeroMem)
                Intrinsics.ClearBits(address.ToPointer(), size);
            owner = true;
        }
        public long Size => SizeOf(Length);

        public int Length { get; private set; }

        internal void Reallocate(int length)
        {
            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            if (address == default)
                throw new ObjectDisposedException(GetType().Name);
            long oldSize = Size, newSize = SizeOf(Length = length);
            address = Marshal.ReAllocHGlobal(address, new IntPtr(newSize));
            var diff = newSize - oldSize;
            if (diff > 0L)
                GC.AddMemoryPressure(diff);
            else if (diff < 0L)
                GC.RemoveMemoryPressure(Math.Abs(diff));
        }

        public unsafe sealed override Span<T> GetSpan() => new Span<T>(address.ToPointer(), Length);

        public unsafe sealed override MemoryHandle Pin(int elementIndex = 0)
        {
            if (address == default)
                throw new ObjectDisposedException(GetType().Name);
            return new MemoryHandle((T*)address.ToPointer() + elementIndex);
        }

        public sealed override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
            if (address != default && owner)
            {
                Marshal.FreeHGlobal(address);
                GC.RemoveMemoryPressure(Size);
            }
            address = default;
        }
    }
}
