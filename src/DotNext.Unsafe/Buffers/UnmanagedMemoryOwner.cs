﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace DotNext.Buffers
{
    using Runtime.InteropServices;

    /// <summary>
    /// Represents unmanaged memory access that allows
    /// to obtain <see cref="Memory{T}"/> pointing to the
    /// unmanaged memory.
    /// </summary>
    /// <typeparam name="T">The type of elements to store in memory.</typeparam>
    internal sealed class UnmanagedMemoryOwner<T> : UnmanagedMemory<T>, IUnmanagedMemoryOwner<T>
        where T : unmanaged
    {
        private readonly bool fromPool;

        internal Action<IUnmanagedMemoryOwner<T>>? OnDisposed;

        internal UnmanagedMemoryOwner(int length, bool zeroMem, bool fromPool) : base(length, zeroMem) => this.fromPool = fromPool;

        unsafe object ICloneable.Clone()
        {
            var copy = new UnmanagedMemoryOwner<T>(Length, false, fromPool);
            Buffer.MemoryCopy(address.ToPointer(), copy.address.ToPointer(), Size, Size);
            return copy;
        }

        Pointer<byte> IUnmanagedMemory.Pointer => new Pointer<byte>(address);

        /// <summary>
        /// Gets a span of bytes from the current instance.
        /// </summary>
        public unsafe Span<byte> Bytes => address == default ? default : new Span<byte>(address.ToPointer(), checked((int)Size));

        /// <summary>
        /// Gets a pointer to the allocated unmanaged memory.
        /// </summary>
        public Pointer<T> Pointer => new Pointer<T>(address);

        Span<T> IUnmanagedArray<T>.Span => GetSpan();

        /// <summary>
        /// Represents unmanaged memory as stream.
        /// </summary>
        /// <returns>The stream of unmanaged memory.</returns>
        public unsafe Stream AsStream() => Pointer.AsStream(Size);

        /// <summary>
        /// Sets all bits of allocated memory to zero.
        /// </summary>
        /// <exception cref="ObjectDisposedException">The underlying unmanaged memory is released.</exception>
        public unsafe void Clear()
        {
            if (address == default)
                throw new ObjectDisposedException(GetType().Name);
            Runtime.Intrinsics.ClearBits(address.ToPointer(), Size);
        }

        /// <summary>
        /// Gets enumerator over all elements located in the unmanaged memory.
        /// </summary>
        /// <returns>The enumerator over all elements in the unmanaged memory.</returns>
        public Pointer<T>.Enumerator GetEnumerator() => Pointer.GetEnumerator(Length);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Releases unmanaged memory that was allocated by this object.
        /// </summary>
        /// <param name="disposing"><see langword="true"/> to release all resources; <see langword="false"/> to release unmanaged memory only.</param>
        protected override void Dispose(bool disposing)
        {
            OnDisposed?.Invoke(this);
            OnDisposed = null;
            base.Dispose(disposing);
        }

        void IUnmanagedMemoryOwner<T>.Reallocate(int length)
        {
            if (fromPool)
                throw new NotSupportedException();
            Reallocate(length);
        }
    }
}