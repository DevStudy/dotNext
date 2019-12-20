using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using Xunit;

namespace DotNext
{
    [ExcludeFromCodeCoverage]
    public sealed class DisposableConceptTest : Assert
    {
        public struct DisposableStruct : IDisposable
        {
            public int Disposed;

            void IDisposable.Dispose() => Disposed += 1;
        }

        public struct DisposableStruct2
        {
            public int Disposed;

            public void Dispose() => Disposed += 1;
        }

        [Fact]
        public static void IDisposableStruct()
        {
            var s = new DisposableStruct();
            Disposable<DisposableStruct>.Dispose(s);
            Equal(1, s.Disposed);
            Disposable<DisposableStruct>.Dispose(s);
            Equal(2, s.Disposed);
        }

        [Fact]
        public static void DisposePattern()
        {
            var s = new DisposableStruct2();
            Disposable<DisposableStruct2>.Dispose(s);
            Equal(1, s.Disposed);
            Disposable<DisposableStruct2>.Dispose(s);
            Equal(2, s.Disposed);
        }

        [Fact]
        public static void MemoryStreamTest()
        {
            var ms = new MemoryStream(new byte[] { 1, 2, 3 });
            Disposable<MemoryStream>.Dispose(ms);
            Throws<ObjectDisposedException>(() => ms.ReadByte());
        }

        private sealed class DisposeCallback : Disposable
        {
            private readonly ManualResetEventSlim disposeSignal;

            internal DisposeCallback(ManualResetEventSlim signal)
                => disposeSignal = signal;

            internal void DisposeAsync() => QueueDispose(this);

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    disposeSignal.Set();
                base.Dispose(disposing);
            }
        }

        [Fact]
        public static void QueueDispose()
        {
            using (var resetEvent = new ManualResetEventSlim(false))
            {
                var disposable = new DisposeCallback(resetEvent);
                disposable.DisposeAsync();
                True(resetEvent.Wait(TimeSpan.FromMinutes(2)));
            }
        }
    }
}