﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace DotNext.Net.Cluster.Consensus.Raft
{
    using Collections.Generic;
    using IO;
    using IO.Log;
    using Replication;
    using Threading;

    /// <summary>
    /// Represents general purpose persistent audit trail compatible with Raft algorithm.
    /// </summary>
    /// <remarks>
    /// The layout of of the audit trail file system:
    /// <list type="table">
    /// <item>
    /// <term>node.state</term>
    /// <description>file containing internal state of Raft node</description>
    /// </item>
    /// <item>
    /// <term>&lt;partition&gt;</term>
    /// <description>file containing log partition with log records</description>
    /// </item>
    /// <item>
    /// <term>snapshot</term>
    /// <description>file containing snapshot</description>
    /// </item>
    /// </list>
    /// The audit trail supports log compaction. However, it doesn't know how to interpret and reduce log records during compaction.
    /// To do that, you can override <see cref="CreateSnapshotBuilder"/> method and implement state machine logic.
    /// </remarks>
    public partial class PersistentState : Disposable, IPersistentState
    {
        private Snapshot snapshot;
        private readonly DirectoryInfo location;
        private readonly AsyncManualResetEvent commitEvent;
        private readonly AsyncSharedLock syncRoot;
        private readonly IRaftLogEntry initialEntry;
        private readonly long initialSize;
        private readonly MemoryPool<LogEntry> entryPool;
        private readonly MemoryPool<LogEntryMetadata>? metadataPool;
        private readonly StreamSegment nullSegment;
        private readonly int bufferSize;

        /// <summary>
        /// Initializes a new persistent audit trail.
        /// </summary>
        /// <param name="path">The path to the folder to be used by audit trail.</param>
        /// <param name="recordsPerPartition">The maximum number of log entries that can be stored in the single file called partition.</param>
        /// <param name="configuration">The configuration of the persistent audit trail.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="recordsPerPartition"/> is less than 2.</exception>
        public PersistentState(DirectoryInfo path, int recordsPerPartition, Options? configuration = null)
        {
            if (configuration is null)
                configuration = new Options();
            if (recordsPerPartition < 2L)
                throw new ArgumentOutOfRangeException(nameof(recordsPerPartition));
            if (!path.Exists)
                path.Create();
            bufferSize = configuration.BufferSize;
            location = path;
            this.recordsPerPartition = recordsPerPartition;
            initialSize = configuration.InitialPartitionSize;
            commitEvent = new AsyncManualResetEvent(false);
            sessionManager = new DataAccessSessionManager(configuration.MaxConcurrentReads, configuration.CreateMemoryPool<byte>, bufferSize);
            syncRoot = new AsyncSharedLock(sessionManager.Capacity);
            entryPool = configuration.CreateMemoryPool<LogEntry>();
            metadataPool = configuration.UseCaching ? configuration.CreateMemoryPool<LogEntryMetadata>() : null;
            nullSegment = new StreamSegment(Stream.Null);
            initialEntry = new LogEntry(nullSegment, sessionManager.WriteSession.Buffer, new LogEntryMetadata());
            //sorted dictionary to improve performance of log compaction and snapshot installation procedures
            partitionTable = new SortedDictionary<long, Partition>();
            //load all partitions from file system
            foreach (var file in path.EnumerateFiles())
                if (long.TryParse(file.Name, out var partitionNumber))
                {
                    var partition = new Partition(file.Directory, bufferSize, recordsPerPartition, partitionNumber, metadataPool, sessionManager.Capacity);
                    partition.PopulateCache(sessionManager.WriteSession);
                    partitionTable[partitionNumber] = partition;
                }
            state = new NodeState(path, AsyncLock.Exclusive(syncRoot));
            snapshot = new Snapshot(path, bufferSize, sessionManager.Capacity);
            snapshot.PopulateCache(sessionManager.WriteSession);
        }

        /// <summary>
        /// Initializes a new persistent audit trail.
        /// </summary>
        /// <param name="path">The path to the folder to be used by audit trail.</param>
        /// <param name="recordsPerPartition">The maximum number of log entries that can be stored in the single file called partition.</param>
        /// <param name="configuration">The configuration of the persistent audit trail.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="recordsPerPartition"/> is less than 2.</exception>
        public PersistentState(string path, int recordsPerPartition, Options? configuration = null)
            : this(new DirectoryInfo(path), recordsPerPartition, configuration)
        {
        }

        /// <summary>
        /// Gets the lock used by this object for write operations.
        /// </summary>
        protected AsyncLock WriteLock => AsyncLock.Exclusive(syncRoot);

        /// <summary>
        /// Gets the lock used by this object for read operations.
        /// </summary>
        protected AsyncLock ReadLock => AsyncLock.Weak(syncRoot);

        /// <summary>
        /// Gets the buffer that can be used to perform I/O operations.
        /// </summary>
        /// <remarks>
        /// The buffer cannot be used concurrently. Access to it should be synchronized
        /// using <see cref="WriteLock"/> property.
        /// </remarks>
        protected Memory<byte> Buffer => sessionManager.WriteSession.Buffer;

        private Partition CreatePartition(long partitionNumber)
            => new Partition(location, Buffer.Length, recordsPerPartition, partitionNumber, metadataPool, sessionManager.Capacity);

        private LogEntry First
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Unsafe.Unbox<LogEntry>(initialEntry);
        }

        private async ValueTask<TResult> ReadAsync<TReader, TResult>(TReader reader, DataAccessSession session, long startIndex, long endIndex, CancellationToken token)
            where TReader : ILogEntryConsumer<IRaftLogEntry, TResult>
        {
            if (startIndex > state.LastIndex)
                throw new IndexOutOfRangeException(ExceptionMessages.InvalidEntryIndex(endIndex));
            if (endIndex > state.LastIndex)
                throw new IndexOutOfRangeException(ExceptionMessages.InvalidEntryIndex(endIndex));
            var length = endIndex - startIndex + 1L;
            if (length > int.MaxValue)
                throw new InternalBufferOverflowException(ExceptionMessages.RangeTooBig);
            LogEntry entry;
            ValueTask<TResult> result;
            if (partitionTable.Count > 0)
                using (var list = entryPool.Rent((int)length))
                {
                    var listIndex = 0;
                    for (Partition? partition = null; startIndex <= endIndex; list.Memory.Span[listIndex++] = entry, startIndex++)
                        if (startIndex == 0L)   //handle ephemeral entity
                            entry = First;
                        else if (TryGetPartition(startIndex, ref partition, out var switched)) //handle regular record
                            entry = (await partition.ReadAsync(session, startIndex, true, switched, token).ConfigureAwait(false)).Value;
                        else if (snapshot.Length > 0 && startIndex <= state.CommitIndex)    //probably the record is snapshotted
                        {
                            entry = await snapshot.ReadAsync(session, token).ConfigureAwait(false);
                            //skip squashed log entries
                            startIndex = state.CommitIndex - (state.CommitIndex + 1) % recordsPerPartition;
                        }
                        else
                            break;
                    result = reader.ReadAsync<LogEntry, InMemoryList<LogEntry>>(list.Memory.Slice(0, listIndex), list.Memory.Span[0].SnapshotIndex, token);
                }
            else if (snapshot.Length > 0)
            {
                entry = await snapshot.ReadAsync(session, token).ConfigureAwait(false);
                result = reader.ReadAsync<LogEntry, SingletonEntryList<LogEntry>>(new SingletonEntryList<LogEntry>(entry), entry.SnapshotIndex, token);
            }
            else
                result = startIndex == 0L ? reader.ReadAsync<LogEntry, SingletonEntryList<LogEntry>>(new SingletonEntryList<LogEntry>(First), null, token) : reader.ReadAsync<LogEntry, LogEntry[]>(Array.Empty<LogEntry>(), null, token);
            return await result.ConfigureAwait(false);
        }

        /// <summary>
        /// Gets log entries in the specified range.
        /// </summary>
        /// <remarks>
        /// This method may return less entries than <c>endIndex - startIndex + 1</c>. It may happen if the requested entries are committed entries and squashed into the single entry called snapshot.
        /// In this case the first entry in the collection is a snapshot entry. Additionally, the caller must call <see cref="IDisposable.Dispose"/> to release resources associated
        /// with the audit trail segment with entries.
        /// </remarks>
        /// <typeparam name="TReader">The type of the reader.</typeparam>
        /// <typeparam name="TResult">The type of the result.</typeparam>
        /// <param name="reader">The reader of the log entries.</param>
        /// <param name="startIndex">The index of the first requested log entry, inclusively.</param>
        /// <param name="endIndex">The index of the last requested log entry, inclusively.</param>
        /// <param name="token">The token that can be used to cancel the operation.</param>
        /// <returns>The collection of log entries.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="startIndex"/> or <paramref name="endIndex"/> is negative.</exception>
        /// <exception cref="IndexOutOfRangeException"><paramref name="endIndex"/> is greater than the index of the last added entry.</exception>
        public async ValueTask<TResult> ReadAsync<TReader, TResult>(TReader reader, long startIndex, long endIndex, CancellationToken token)
            where TReader : ILogEntryConsumer<IRaftLogEntry, TResult>
        {
            if (startIndex < 0L)
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            if (endIndex < 0L)
                throw new ArgumentOutOfRangeException(nameof(endIndex));
            if (endIndex < startIndex)
                return await reader.ReadAsync<LogEntry, LogEntry[]>(Array.Empty<LogEntry>(), null, token).ConfigureAwait(false);
            //obtain weak lock as read lock
            await syncRoot.AcquireAsync(false, token).ConfigureAwait(false);
            var session = sessionManager.OpenSession(bufferSize);
            try
            {
                return await ReadAsync<TReader, TResult>(reader, session, startIndex, endIndex, token).ConfigureAwait(false);
            }
            finally
            {
                sessionManager.CloseSession(session);  //return session back to the pool
                syncRoot.Release();
            }
        }

        /// <summary>
        /// Gets log entries starting from the specified index to the last log entry.
        /// </summary>
        /// <typeparam name="TReader">The type of the reader.</typeparam>
        /// <typeparam name="TResult">The type of the result.</typeparam>
        /// <param name="reader">The reader of the log entries.</param>
        /// <param name="startIndex">The index of the first requested log entry, inclusively.</param>
        /// <param name="token">The token that can be used to cancel the operation.</param>
        /// <returns>The collection of log entries.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="startIndex"/> is negative.</exception>
        public async ValueTask<TResult> ReadAsync<TReader, TResult>(TReader reader, long startIndex, CancellationToken token)
            where TReader : ILogEntryConsumer<IRaftLogEntry, TResult>
        {
            if (startIndex < 0L)
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            await syncRoot.AcquireAsync(false, token).ConfigureAwait(false);
            var session = sessionManager.OpenSession(bufferSize);
            try
            {
                return await ReadAsync<TReader, TResult>(reader, session, startIndex, state.LastIndex, token).ConfigureAwait(false);
            }
            finally
            {
                sessionManager.CloseSession(session);
                syncRoot.Release();
            }
        }

        private async ValueTask InstallSnapshot<TSnapshot>(TSnapshot snapshot, long snapshotIndex)
            where TSnapshot : IRaftLogEntry
        {
            //0. The snapshot can be installed only if the partitions were squashed on the sender side
            //therefore, snapshotIndex should be a factor of recordsPerPartition
            if ((snapshotIndex + 1) % recordsPerPartition != 0)
                throw new ArgumentOutOfRangeException(nameof(snapshotIndex));
            //1. Save the snapshot into temporary file to avoid corruption caused by network connection
            string tempSnapshotFile, snapshotFile = this.snapshot.Name;
            using (var tempSnapshot = new Snapshot(location, Buffer.Length, 0, true))
            {
                tempSnapshotFile = tempSnapshot.Name;
                await tempSnapshot.WriteAsync(sessionManager.WriteSession, snapshot, snapshotIndex, CancellationToken.None).ConfigureAwait(false);
            }
            //2. Delete existing snapshot file
            this.snapshot.Dispose();
            /*
             * Swapping snapshot file is unsafe operation because of potential disk I/O failures.
             * However, event if swapping will fail then it can be recovered manually just by renaming 'snapshot.new' file
             * into 'snapshot'. Both versions of snapshot file stay consistent. That's why stream copying is not an option.
             */
            try
            {
                File.Delete(snapshotFile);
                File.Move(tempSnapshotFile, snapshotFile);
            }
            catch (Exception e)
            {
                Environment.FailFast(LogMessages.SnapshotInstallationFailed, e);
            }
            this.snapshot = new Snapshot(location, Buffer.Length, sessionManager.Capacity);
            this.snapshot.PopulateCache(sessionManager.WriteSession);
            //3. Identify all partitions to be replaced by snapshot
            var compactionScope = new Dictionary<long, Partition>();
            foreach (var (partitionNumber, partition) in partitionTable)
                if (partition.LastIndex <= snapshotIndex)
                    compactionScope.Add(partitionNumber, partition);
                else
                    break;  //enumeration is sorted by partition number so we don't need to enumerate over all partitions
            //4. Delete these partitions
            RemovePartitions(compactionScope);
            compactionScope.Clear();
            //5. Apply snapshot to the underlying state machine
            state.CommitIndex = snapshotIndex;
            state.LastIndex = Math.Max(snapshotIndex, state.LastIndex);

            await ApplyAsync(await this.snapshot.ReadAsync(sessionManager.WriteSession, CancellationToken.None).ConfigureAwait(false));
            state.LastApplied = snapshotIndex;
            state.Flush();
            await FlushAsync().ConfigureAwait(false);
            commitEvent.Set(true);
        }

        private async ValueTask AppendAsync<TEntry>(ILogEntryProducer<TEntry> supplier, long startIndex, bool skipCommitted, CancellationToken token)
            where TEntry : IRaftLogEntry
        {
            if (startIndex > state.LastIndex + 1)
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            Partition? partition;
            for (partition = null; !token.IsCancellationRequested && await supplier.MoveNextAsync().ConfigureAwait(false); state.LastIndex = startIndex++)
                if (supplier.Current.IsSnapshot)
                    throw new InvalidOperationException(ExceptionMessages.SnapshotDetected);
                else if (startIndex > state.CommitIndex)
                {
                    await GetOrCreatePartitionAsync(startIndex, ref partition).ConfigureAwait(false);
                    await partition.WriteAsync(sessionManager.WriteSession, supplier.Current, startIndex).ConfigureAwait(false);
                }
                else if (!skipCommitted)
                    throw new InvalidOperationException(ExceptionMessages.InvalidAppendIndex);
            await FlushAsync(partition).ConfigureAwait(false);
            //flush updated state
            state.Flush();
            token.ThrowIfCancellationRequested();
        }

        async ValueTask IAuditTrail<IRaftLogEntry>.AppendAsync<TEntry>(ILogEntryProducer<TEntry> entries, long startIndex, bool skipCommitted, CancellationToken token)
        {
            if (entries.RemainingCount == 0L)
                return;
            await syncRoot.AcquireAsync(true, CancellationToken.None).ConfigureAwait(false);
            try
            {
                await AppendAsync(entries, startIndex, skipCommitted, token).ConfigureAwait(false);
            }
            finally
            {
                syncRoot.Release();
            }
        }

        /// <summary>
        /// Adds uncommitted log entry to the end of this log.
        /// </summary>
        /// <remarks>
        /// This is the only method that can be used for snapshot installation.
        /// The behavior of the method depends on the <see cref="ILogEntry.IsSnapshot"/> property.
        /// If log entry is a snapshot then the method erases all committed log entries prior to <paramref name="startIndex"/>.
        /// If it is not, the method behaves in the same way as <see cref="AppendAsync{TEntryImpl}(ILogEntryProducer{TEntryImpl}, long, bool, CancellationToken)"/>.
        /// </remarks>
        /// <typeparam name="TEntry">The actual type of the supplied log entry.</typeparam>
        /// <param name="entry">The uncommitted log entry to be added into this audit trail.</param>
        /// <param name="startIndex">The index from which all previous log entries should be dropped and replaced with the new entry.</param>
        /// <returns>The task representing asynchronous state of the method.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="entry"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="startIndex"/> is less than the index of the last committed entry and <paramref name="entry"/> is not a snapshot.</exception>
        public async ValueTask AppendAsync<TEntry>(TEntry entry, long startIndex)
            where TEntry : IRaftLogEntry
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            await syncRoot.AcquireAsync(true, CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (startIndex <= state.CommitIndex)
                    throw new InvalidOperationException(ExceptionMessages.InvalidAppendIndex);
                else if (entry.IsSnapshot)
                    await InstallSnapshot(entry, startIndex).ConfigureAwait(false);
                else if (startIndex <= state.CommitIndex)
                    throw new InvalidOperationException(ExceptionMessages.InvalidAppendIndex);
                else if (startIndex > state.LastIndex + 1)
                    throw new ArgumentOutOfRangeException(nameof(startIndex));
                else
                {
                    GetOrCreatePartition(startIndex, out var partition);
                    await partition.WriteAsync(sessionManager.WriteSession, entry, startIndex).ConfigureAwait(false);
                    await partition.FlushAsync().ConfigureAwait(false);
                    state.LastIndex = startIndex;
                    state.Flush();
                }
            }
            finally
            {
                syncRoot.Release();
            }
        }

        /// <summary>
        /// Adds uncommitted log entries to the end of this log.
        /// </summary>
        /// <remarks>
        /// This method should updates cached value provided by method <see cref="IAuditTrail.GetLastIndex"/> called with argument of value <see langword="false"/>.
        /// </remarks>
        /// <typeparam name="TEntry">The actual type of the log entry returned by the supplier.</typeparam>
        /// <param name="entries">The entries to be added into this log.</param>
        /// <param name="token">The token that can be used to cancel the operation.</param>
        /// <returns>Index of the first added entry.</returns>
        /// <exception cref="ArgumentException"><paramref name="entries"/> is empty.</exception>
        /// <exception cref="InvalidOperationException">The collection of entries contains the snapshot entry.</exception>
        public async ValueTask<long> AppendAsync<TEntry>(ILogEntryProducer<TEntry> entries, CancellationToken token = default)
            where TEntry : IRaftLogEntry
        {
            if (entries.RemainingCount == 0L)
                throw new ArgumentException(ExceptionMessages.EntrySetIsEmpty);
            await syncRoot.AcquireAsync(true, token).ConfigureAwait(false);
            var startIndex = state.LastIndex + 1L;
            try
            {
                await AppendAsync(entries, startIndex, false, token).ConfigureAwait(false);
            }
            finally
            {
                syncRoot.Release();
            }
            return startIndex;
        }

        /// <summary>
        /// Dropes the uncommitted entries starting from the specified position to the end of the log.
        /// </summary>
        /// <param name="startIndex">The index of the first log entry to be dropped.</param>
        /// <param name="token">The token that can be used to cancel the operation.</param>
        /// <returns>The actual number of dropped entries.</returns>
        /// <exception cref="InvalidOperationException"><paramref name="startIndex"/> represents index of the committed entry.</exception>
        public async ValueTask<long> DropAsync(long startIndex, CancellationToken token)
        {
            long count;
            await syncRoot.AcquireAsync(true, token).ConfigureAwait(false);
            try
            {
                if (startIndex <= state.CommitIndex)
                    throw new InvalidOperationException(ExceptionMessages.InvalidAppendIndex);
                if (startIndex > state.LastIndex)
                    return 0L;
                count = state.LastIndex - startIndex + 1L;
                state.LastIndex = startIndex - 1L;
                state.Flush();
                //find partitions to be deleted
                var partitionNumber = Math.DivRem(startIndex, recordsPerPartition, out var remainder);
                //take the next partition if startIndex is not a beginning of the calculated partition
                partitionNumber += (remainder > 0L).ToInt32();
                for (Partition partition; partitionTable.TryGetValue(partitionNumber, out partition); partitionNumber++)
                {
                    var fileName = partition.Name;
                    partitionTable.Remove(partitionNumber);
                    partition.Dispose();
                    File.Delete(fileName);
                }
            }
            finally
            {
                syncRoot.Release();
            }
            return count;
        }

        /// <summary>
        /// Waits for the commit.
        /// </summary>
        /// <param name="index">The index of the log record to be committed.</param>
        /// <param name="timeout">The timeout used to wait for the commit.</param>
        /// <param name="token">The token that can be used to cancel waiting.</param>
        /// <returns>The task representing waiting operation.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is less than 1.</exception>
        /// <exception cref="OperationCanceledException">The operation has been cancelled.</exception>
        public Task WaitForCommitAsync(long index, TimeSpan timeout, CancellationToken token)
            => index >= 0L ? CommitEvent.WaitForCommitAsync(this, commitEvent, index, timeout, token) : Task.FromException(new ArgumentOutOfRangeException(nameof(index)));

        private async ValueTask ForceCompaction(SnapshotBuilder builder, CancellationToken token)
        {
            //1. Find the partitions that can be compacted
            var compactionScope = new SortedDictionary<long, Partition>();
            foreach (var (partNumber, partition) in partitionTable)
            {
                token.ThrowIfCancellationRequested();
                if (partition.LastIndex <= state.CommitIndex)
                    compactionScope.Add(partNumber, partition);
                else
                    break;  //enumeration is sorted by partition number so we don't need to enumerate over all partitions
            }
            Debug.Assert(compactionScope.Count > 0);
            //2. Do compaction
            var snapshotIndex = 0L;
            foreach (var partition in compactionScope.Values)
            {
                await partition.FlushAsync(sessionManager.WriteSession, token).ConfigureAwait(false);
                for (var i = 0; i < partition.Capacity; i++)
                    if (partition.FirstIndex > 0L || i > 0L) //ignore the ephemeral entry
                    {
                        var entry = (await partition.ReadAsync(sessionManager.WriteSession, i, false, false, token).ConfigureAwait(false)).Value;
                        entry.AdjustPosition();
                        await builder.ApplyCoreAsync(entry).ConfigureAwait(false);
                    }
                snapshotIndex = partition.LastIndex;
            }
            //3. Persist snapshot
            await snapshot.WriteAsync(sessionManager.WriteSession, builder, snapshotIndex, token).ConfigureAwait(false);
            await snapshot.FlushAsync().ConfigureAwait(false);
            //4. Remove snapshotted partitions
            RemovePartitions(compactionScope);
            compactionScope.Clear();
        }

        private ValueTask ForceCompaction(CancellationToken token)
        {
            SnapshotBuilder? builder;
            if (state.CommitIndex - snapshot.Index > recordsPerPartition && (builder = CreateSnapshotBuilder()) != null)
                try
                {
                    return ForceCompaction(builder, token);
                }
                finally
                {
                    builder.Dispose();
                }
            else
                return default;
        }

        private async ValueTask<long> CommitAsync(long? endIndex, CancellationToken token)
        {
            long count;
            await syncRoot.AcquireAsync(true, token).ConfigureAwait(false);
            var startIndex = state.CommitIndex + 1L;
            try
            {
                count = (endIndex ?? GetLastIndex(false)) - startIndex + 1L;
                if (count > 0)
                {
                    state.CommitIndex = startIndex + count - 1;
                    await ApplyAsync(token).ConfigureAwait(false);
                    await ForceCompaction(token).ConfigureAwait(false);
                    commitEvent.Set(true);
                }
            }
            finally
            {
                syncRoot.Release();
            }
            return Math.Max(count, 0L);
        }

        /// <summary>
        /// Commits log entries into the underlying storage and marks these entries as committed.
        /// </summary>
        /// <remarks>
        /// This method should updates cached value provided by method <see cref="IAuditTrail.GetLastIndex"/> called with argument of value <see langword="true"/>.
        /// Additionally, it may force log compaction and squash all committed entries into single entry called snapshot.
        /// </remarks>
        /// <param name="endIndex">The index of the last entry to commit, inclusively; if <see langword="null"/> then commits all log entries started from the first uncommitted entry to the last existing log entry.</param>
        /// <param name="token">The token that can be used to cancel the operation.</param>
        /// <returns>The actual number of committed entries.</returns>
        /// <exception cref="OperationCanceledException">The operation has been cancelled.</exception>
        public ValueTask<long> CommitAsync(long endIndex, CancellationToken token) => CommitAsync(new long?(endIndex), token);

        /// <summary>
        /// Commits log entries into the underlying storage and marks these entries as committed.
        /// </summary>
        /// <remarks>
        /// This method should updates cached value provided by method <see cref="IAuditTrail.GetLastIndex"/> called with argument of value <see langword="true"/>.
        /// Additionally, it may force log compaction and squash all committed entries into single entry called snapshot.
        /// </remarks>
        /// <param name="token">The token that can be used to cancel the operation.</param>
        /// <returns>The actual number of committed entries.</returns>
        /// <exception cref="OperationCanceledException">The operation has been cancelled.</exception>
        public ValueTask<long> CommitAsync(CancellationToken token) => CommitAsync(null, token);

        /// <summary>
        /// Applies the command represented by the log entry to the underlying database engine.
        /// </summary>
        /// <param name="entry">The entry to be applied to the state machine.</param>
        /// <remarks>
        /// The base method does nothing so you don't need to call base implementation.
        /// </remarks>
        /// <returns>The task representing asynchronous execution of this method.</returns>
        protected virtual ValueTask ApplyAsync(LogEntry entry) => new ValueTask();

        /// <summary>
        /// Flushes the underlying data storage.
        /// </summary>
        /// <returns>The task representing asynchronous execution of this method.</returns>
        protected virtual ValueTask FlushAsync() => new ValueTask();

        private async ValueTask ApplyAsync(CancellationToken token)
        {
            Partition? partition = null;
            for (var i = state.LastApplied + 1L; i <= state.CommitIndex; state.LastApplied = i++)
                if (TryGetPartition(i, ref partition, out var switched))
                {
                    var entry = (await partition.ReadAsync(sessionManager.WriteSession, i, true, switched, token).ConfigureAwait(false)).Value;
                    entry.AdjustPosition();
                    await ApplyAsync(entry).ConfigureAwait(false);
                }
                else
                    Debug.Fail($"Log entry with index {i} doesn't have partition");
            state.Flush();
            await FlushAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Ensures that all committed entries are applied to the underlying data state machine known as database engine.
        /// </summary>
        /// <param name="token">The token that can be used to cancel the operation.</param>
        /// <returns>The task representing asynchronous state of the method.</returns>
        /// <exception cref="OperationCanceledException">The operation has been cancelled.</exception>
        public async Task EnsureConsistencyAsync(CancellationToken token)
        {
            await syncRoot.AcquireAsync(true, token).ConfigureAwait(false);
            try
            {
                await ApplyAsync(token).ConfigureAwait(false);
            }
            finally
            {
                syncRoot.Release();
            }
        }

        ref readonly IRaftLogEntry IAuditTrail<IRaftLogEntry>.First => ref initialEntry;

        bool IPersistentState.IsVotedFor(IRaftClusterMember? member) => state.IsVotedFor(member?.Endpoint);

        long IPersistentState.Term => state.Term;

        ValueTask<long> IPersistentState.IncrementTermAsync() => state.IncrementTermAsync();

        ValueTask IPersistentState.UpdateTermAsync(long term) => state.UpdateTermAsync(term);

        ValueTask IPersistentState.UpdateVotedForAsync(IRaftClusterMember? member) => state.UpdateVotedForAsync(member?.Endpoint);

        /// <summary>
        /// Releases all resources associated with this audit trail.
        /// </summary>
        /// <param name="disposing"><see langword="true"/> if called from <see cref="IDisposable.Dispose()"/>; <see langword="false"/> if called from finalizer.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var partition in partitionTable.Values)
                    partition.Dispose();
                sessionManager.Dispose();
                partitionTable.Clear();
                state.Dispose();
                commitEvent.Dispose();
                syncRoot.Dispose();
                snapshot?.Dispose();
                nullSegment.Dispose();
                entryPool.Dispose();
                metadataPool?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}