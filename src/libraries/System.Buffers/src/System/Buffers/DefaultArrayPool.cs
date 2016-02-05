// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace System.Buffers
{
    internal sealed class DefaultArrayPool<T> : ArrayPool<T>
    {
        /// <summary>The default maximum number of arrays per bucket that are available for rent.</summary>
        private const int DefaultMaxNumberOfArraysPerBucket = 50;
        /// <summary>The default maximum length of each array in the pool (2^20).</summary>
        private const int DefaultMaxArrayLength = 1024 * 1024;
        /// <summary>The minimum length of an array in the pool.</summary>
        private const int MinimumArrayLength = 16;

        private readonly DefaultArrayPoolBucket<T>[] _buckets;

        internal DefaultArrayPool() : this(DefaultMaxArrayLength, DefaultMaxNumberOfArraysPerBucket)
        {
        }

        internal DefaultArrayPool(int maxLength, int arraysPerBucket)
        {
            if (maxLength <= 0)
                throw new ArgumentOutOfRangeException("maxLength");
            if (arraysPerBucket <= 0)
                throw new ArgumentOutOfRangeException("arraysPerBucket");

            // Our bucketing algorithm has a minimum length of 16
            if (maxLength < MinimumArrayLength)
                maxLength = MinimumArrayLength;

            int maxBuckets = Utilities.SelectBucketIndex(maxLength);
            _buckets = new DefaultArrayPoolBucket<T>[maxBuckets + 1];
            for (int i = 0; i < _buckets.Length; i++)
                _buckets[i] = new DefaultArrayPoolBucket<T>(Utilities.GetMaxSizeForBucket(i), arraysPerBucket, Utilities.GetPoolId(this));
        }

        public override T[] Rent(int minimumLength)
        {
            if (minimumLength <= 0)
                throw new ArgumentOutOfRangeException("minimumLength");
                
            var log = ArrayPoolEventSource.Log;

            T[] buffer = null;
            int index = Utilities.SelectBucketIndex(minimumLength);
            if (index < _buckets.Length)
            {
                // Search for an array starting at the 'index' bucket. If the bucket
                // is empty, bump up to the next higher bucket and try that one
                for (int i = index; i < _buckets.Length; i++)
                {
                    buffer = _buckets[i].Rent();

                    // If the bucket has an array left and returned it, give it to the caller
                    if (buffer != null)
                    {
                        if (log.IsEnabled())
                        {
                            log.BufferRented(Utilities.GetBufferId(buffer), buffer.Length, Utilities.GetBucketId(_buckets[i]), Utilities.GetPoolId(this));
                        }
                        return buffer;
                    }
                }
            }

            // Gettings here means we have too big of a request OR all the buckets from 
            // index through _buckets.Length are taken so we need to allocate a buffer on-demand.
            buffer = new T[Utilities.GetMaxSizeForBucket(index)];
            if (log.IsEnabled())
            {
                int maxLength = Utilities.GetMaxSizeForBucket(_buckets.Length);
                log.BufferAllocated(
                    Utilities.GetBufferId(buffer),
                    buffer.Length,
                    Utilities.GetPoolId(this),
                    -1, // no bucket for an on-demand allocated buffer,
                    buffer.Length > maxLength ? 
                        ArrayPoolEventSource.BufferAllocationReason.OverMaximumSize :
                        ArrayPoolEventSource.BufferAllocationReason.PoolExhausted);
            }

            return buffer;
        }

        public override void Return(T[] buffer, bool clearArray = false)
        {
            if (buffer == null)
                throw new ArgumentNullException("buffer");

            // If we can tell that the buffer was allocated, drop it. Otherwise, check if we have space in the pool
            int bucket = Utilities.SelectBucketIndex(buffer.Length);
            if (bucket < _buckets.Length)
            {
                // Clear the array if the user requests
                if (clearArray) Array.Clear(buffer, 0, buffer.Length);

                _buckets[bucket].Return(buffer);
            }

            var log = ArrayPoolEventSource.Log;
            if (log.IsEnabled())
                log.BufferReturned(Utilities.GetBufferId(buffer), Utilities.GetPoolId(this));
        }
    }
}
