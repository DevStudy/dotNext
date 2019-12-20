﻿using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace DotNext.Threading
{
    [ExcludeFromCodeCoverage]
    public sealed class AsyncLockTests : Assert
    {
        [Fact]
        public static async Task EmptyLock()
        {
            var @lock = default(AsyncLock);
            var holder = await @lock.TryAcquireAsync(CancellationToken.None);
            if (holder)
                throw new Exception();

            holder = await @lock.AcquireAsync(CancellationToken.None);
            if (holder)
                throw new Exception();

            holder = await @lock.AcquireAsync(TimeSpan.FromHours(1));
            if (holder)
                throw new Exception();

            holder.Dispose();
        }

        [Fact]
        public static async Task ExclusiveLock()
        {
            using var syncRoot = new AsyncExclusiveLock();
            using var @lock = AsyncLock.Exclusive(syncRoot);
            var holder = await @lock.TryAcquireAsync(CancellationToken.None);
            if (holder) { }
            else throw new Exception();
            True(syncRoot.IsLockHeld);
            holder.Dispose();
            False(syncRoot.IsLockHeld);

            holder = await @lock.AcquireAsync(CancellationToken.None);
            True(syncRoot.IsLockHeld);
            holder.Dispose();
            False(syncRoot.IsLockHeld);
        }

        [Fact]
        public static async Task SemaphoreLock()
        {
            using var sem = new SemaphoreSlim(3);
            using var @lock = AsyncLock.Semaphore(sem);
            var holder = await @lock.TryAcquireAsync(CancellationToken.None);
            if (holder) { }
            else throw new Exception();
            Equal(2, sem.CurrentCount);
            holder.Dispose();
            Equal(3, sem.CurrentCount);
        }
    }
}
