﻿using System;
using System.Buffers;

namespace DotNext.Net.Cluster.Consensus.Raft
{
    public partial class PersistentState
    {
        /// <summary>
        /// Represents configuration options of the persistent audit trail.
        /// </summary>
        public class Options
        {
            private const int MinBufferSize = 128;
            private int bufferSize = 2048;
            private int concurrencyLevel = 3;

            /// <summary>
            /// Gets size of in-memory buffer for I/O operations.
            /// </summary>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is too small.</exception>
            public int BufferSize
            {
                get => bufferSize;
                set
                {
                    if (value < MinBufferSize)
                        throw new ArgumentOutOfRangeException(nameof(value));
                    bufferSize = value;
                }
            }

            /// <summary>
            /// Gets or sets the initial size of the file that holds the partition with log entries.
            /// </summary>
            public long InitialPartitionSize { get; set; } = 0;

            /// <summary>
            /// Enables or disables in-memory cache.
            /// </summary>
            /// <value><see langword="true"/> to in-memory cache for faster read/write of log entries; <see langword="false"/> to reduce the memory by the cost of the performance.</value>
            public bool UseCaching { get; set; } = true;

            /// <summary>
            /// Gets memory pool that is used by Write Ahead Log for its I/O operations.
            /// </summary>
            /// <returns>The instance of memory pool.</returns>
            public virtual MemoryPool<T> CreateMemoryPool<T>() where T : struct => MemoryPool<T>.Shared;

            /// <summary>
            /// Gets or sets the number of possible parallel reads.
            /// </summary>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is less than 1.</exception>
            public int MaxConcurrentReads
            {
                get => concurrencyLevel;
                set
                {
                    if (concurrencyLevel < 1)
                        throw new ArgumentOutOfRangeException(nameof(value));
                    concurrencyLevel = value;
                }
            }
        }
    }
}
