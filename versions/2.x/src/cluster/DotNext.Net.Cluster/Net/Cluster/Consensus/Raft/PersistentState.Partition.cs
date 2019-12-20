﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using static System.Globalization.CultureInfo;
using static System.Runtime.InteropServices.MemoryMarshal;

namespace DotNext.Net.Cluster.Consensus.Raft
{
    using IO;

    public partial class PersistentState
    {
        /*
            Partition file format:
            FileName - number of partition
            Allocation table:
            [struct LogEntryMetadata] X number of entries
            Payload:
            [octet string] X number of entries
         */
        private sealed class Partition : ConcurrentStorageAccess
        {
            internal readonly long FirstIndex;
            internal readonly int Capacity;    //max number of entries
            private readonly IMemoryOwner<LogEntryMetadata>? lookupCache;

            internal Partition(DirectoryInfo location, int bufferSize, int recordsPerPartition, long partitionNumber, MemoryPool<LogEntryMetadata>? cachePool, int readersCount)
                : base(Path.Combine(location.FullName, partitionNumber.ToString(InvariantCulture)), bufferSize, readersCount, FileOptions.RandomAccess | FileOptions.WriteThrough | FileOptions.Asynchronous)
            {
                Capacity = recordsPerPartition;
                FirstIndex = partitionNumber * recordsPerPartition;
                lookupCache = cachePool?.Rent(recordsPerPartition);
            }

            private long PayloadOffset => LogEntryMetadata.Size * (long)Capacity;

            internal long LastIndex => FirstIndex + Capacity - 1;

            internal void Allocate(long initialSize) => SetLength(initialSize + PayloadOffset);

            private void PopulateCache(Span<byte> buffer, Span<LogEntryMetadata> lookupCache)
            {
                for (int index = 0, count; index < lookupCache.Length; index += count)
                {
                    count = Math.Min(buffer.Length / LogEntryMetadata.Size, lookupCache.Length - index);
                    var maxBytes = count * LogEntryMetadata.Size;
                    var source = buffer.Slice(0, maxBytes);
                    if (Read(source) < maxBytes)
                        throw new EndOfStreamException();
                    var destination = AsBytes(lookupCache.Slice(index));
                    source.CopyTo(destination);
                }
            }

            internal override void PopulateCache(in DataAccessSession session)
            {
                if (lookupCache != null)
                    PopulateCache(session.Buffer.Span, lookupCache.Memory.Span.Slice(0, Capacity));
            }

            private async ValueTask<LogEntry?> ReadAsync(StreamSegment reader, Memory<byte> buffer, int index, bool refreshStream, CancellationToken token)
            {
                Debug.Assert(index >= 0 && index < Capacity, $"Invalid index value {index}, offset {FirstIndex}");
                //find pointer to the content
                LogEntryMetadata metadata;
                if (refreshStream)
                    await reader.FlushAsync(token).ConfigureAwait(false);
                if (lookupCache is null)
                {
                    reader.BaseStream.Position = index * LogEntryMetadata.Size;
                    metadata = await reader.BaseStream.ReadAsync<LogEntryMetadata>(buffer, token).ConfigureAwait(false);
                }
                else
                    metadata = lookupCache.Memory.Span[index];
                return metadata.Offset > 0 ? new LogEntry(reader, buffer, metadata) : new LogEntry?();
            }

            internal ValueTask<LogEntry?> ReadAsync(in DataAccessSession session, long index, bool absoluteIndex, bool refreshStream, CancellationToken token)
            {
                //calculate relative index
                if (absoluteIndex)
                    index -= FirstIndex;
                Debug.Assert(index >= 0 && index < Capacity, $"Invalid index value {index}, offset {FirstIndex}");
                return ReadAsync(GetReadSessionStream(session), session.Buffer, (int)index, refreshStream, token);
            }

            private async ValueTask WriteAsync<TEntry>(TEntry entry, int index, Memory<byte> buffer)
                where TEntry : IRaftLogEntry
            {
                //calculate offset of the previous entry
                long offset;
                LogEntryMetadata metadata;
                if (index == 0L || index == 1L && FirstIndex == 0L)
                    offset = PayloadOffset;
                else if (lookupCache is null)
                {
                    //read content offset and the length of the previous entry
                    Position = (index - 1) * LogEntryMetadata.Size;
                    metadata = await this.ReadAsync<LogEntryMetadata>(buffer).ConfigureAwait(false);
                    Debug.Assert(metadata.Offset > 0, "Previous entry doesn't exist for unknown reason");
                    offset = metadata.Length + metadata.Offset;
                }
                else
                {
                    metadata = lookupCache.Memory.Span[index - 1];
                    Debug.Assert(metadata.Offset > 0, "Previous entry doesn't exist for unknown reason");
                    offset = metadata.Length + metadata.Offset;
                }
                //write content
                Position = offset;
                await entry.CopyToAsync(this).ConfigureAwait(false);
                metadata = LogEntryMetadata.Create(entry, offset, Position - offset);
                //record new log entry to the allocation table
                Position = index * LogEntryMetadata.Size;
                await this.WriteAsync(metadata, buffer).ConfigureAwait(false);
                //update cache
                if (lookupCache != null)
                    lookupCache.Memory.Span[index] = metadata;
            }

            internal ValueTask WriteAsync<TEntry>(in DataAccessSession session, TEntry entry, long index)
                where TEntry : IRaftLogEntry
            {
                //calculate relative index
                index -= FirstIndex;
                Debug.Assert(index >= 0 && index < Capacity, $"Invalid index value {index}, offset {FirstIndex}");
                return WriteAsync(entry, (int)index, session.Buffer);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    lookupCache?.Dispose();
                base.Dispose(disposing);
            }
        }

        /*
         * Binary format:
         * [struct SnapshotMetadata] X 1
         * [octet string] X 1
         */
        private sealed class Snapshot : ConcurrentStorageAccess
        {
            private const string FileName = "snapshot";
            private const string TempFileName = "snapshot.new";

            internal Snapshot(DirectoryInfo location, int bufferSize, int readersCount, bool tempSnapshot = false)
                : base(Path.Combine(location.FullName, tempSnapshot ? TempFileName : FileName), bufferSize, readersCount, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.RandomAccess | FileOptions.WriteThrough)
            {
            }

            internal override void PopulateCache(in DataAccessSession session)
                => Index = Length > 0L ? this.Read<SnapshotMetadata>().Index : 0L;

            private async ValueTask WriteAsync<TEntry>(TEntry entry, long index, Memory<byte> buffer, CancellationToken token)
                where TEntry : IRaftLogEntry
            {
                Index = index;
                Position = SnapshotMetadata.Size;
                await entry.CopyToAsync(this, token).ConfigureAwait(false);
                var metadata = SnapshotMetadata.Create(entry, index, Length - SnapshotMetadata.Size);
                Position = 0;
                await this.WriteAsync(metadata, buffer, token).ConfigureAwait(false);
            }

            internal ValueTask WriteAsync<TEntry>(in DataAccessSession session, TEntry entry, long index, CancellationToken token)
                where TEntry : IRaftLogEntry
                => WriteAsync(entry, index, session.Buffer, token);

            private static async ValueTask<LogEntry> ReadAsync(StreamSegment reader, Memory<byte> buffer, CancellationToken token)
            {
                reader.BaseStream.Position = 0;
                //snapshot reader stream may be out of sync with writer stream
                await reader.FlushAsync(token).ConfigureAwait(false);
                return new LogEntry(reader, buffer, await reader.BaseStream.ReadAsync<SnapshotMetadata>(buffer, token).ConfigureAwait(false));
            }

            internal ValueTask<LogEntry> ReadAsync(in DataAccessSession session, CancellationToken token)
                => ReadAsync(GetReadSessionStream(session), session.Buffer, token);

            //cached index of the snapshotted entry
            internal long Index
            {
                get;
                private set;
            }
        }

        private readonly int recordsPerPartition;
        //key is the number of partition
        private readonly IDictionary<long, Partition> partitionTable;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long PartitionOf(long recordIndex) => recordIndex / recordsPerPartition;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGetPartition(long recordIndex, [NotNullWhen(true)]ref Partition? partition)
            => partition != null && recordIndex >= partition.FirstIndex && recordIndex <= partition.LastIndex || partitionTable.TryGetValue(PartitionOf(recordIndex), out partition);

        private bool TryGetPartition(long recordIndex, [NotNullWhen(true)]ref Partition? partition, out bool switched)
        {
            var previous = partition;
            var result = TryGetPartition(recordIndex, ref partition);
            switched = partition != previous;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Task FlushAsync(Partition? partition) => partition is null ? Task.CompletedTask : partition.FlushAsync();

        private void GetOrCreatePartition(long recordIndex, out Partition partition)
        {
            var partitionNumber = PartitionOf(recordIndex);
            if (!partitionTable.TryGetValue(partitionNumber, out partition))
            {
                partition = CreatePartition(partitionNumber);
                partition.Allocate(initialSize);
                partitionTable.Add(partitionNumber, partition);
            }
        }

        private Task GetOrCreatePartitionAsync(long recordIndex, [NotNull]ref Partition? partition)
        {
            Task flushTask;
            if (partition is null || recordIndex < partition.FirstIndex || recordIndex > partition.LastIndex)
            {
                flushTask = FlushAsync(partition);
                GetOrCreatePartition(recordIndex, out partition);
            }
            else
                flushTask = Task.CompletedTask;
            return flushTask;
        }

        private void RemovePartitions(IDictionary<long, Partition> partitions)
        {
            foreach (var (partitionNumber, partition) in partitions)
            {
                partitionTable.Remove(partitionNumber);
                var fileName = partition.Name;
                partition.Dispose();
                File.Delete(fileName);
            }
        }
    }
}
