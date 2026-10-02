using System;
using System.Runtime.CompilerServices;
using ZeroPrimitives.Cryptography;

namespace ZeroAgent.Core.Database
{
    /// <summary>
    /// High-throughput, memory-efficient Bit-Vector Bloom Filter designed for billion-scale key indexing.
    /// Provides deterministic sub-10ns negative existence checks to eliminate 99%+ of disk/MMap lookups.
    /// </summary>
    public sealed class ZabBloomFilter
    {
        private readonly ulong[] _bits;
        private readonly ulong _bitCount;
        private readonly int _hashFunctions;

        public ulong BitCount => _bitCount;
        public int HashFunctionCount => _hashFunctions;
        public int StorageSizeBytes => _bits.Length * sizeof(ulong);

        /// <summary>
        /// Initializes a Bloom Filter sized for expected entries and target false positive rate.
        /// </summary>
        public ZabBloomFilter(long expectedEntries, double falsePositiveRate = 0.01)
        {
            if (expectedEntries <= 0) expectedEntries = 1024;
            if (falsePositiveRate <= 0.0 || falsePositiveRate >= 1.0) falsePositiveRate = 0.01;

            // m = - (n * ln(p)) / (ln(2)^2)
            double m = -((double)expectedEntries * Math.Log(falsePositiveRate)) / 0.480453013918201;
            ulong totalBits = Math.Max(64, (ulong)Math.Ceiling(m));
            // Align to 64-bit boundaries
            ulong ulongCount = (totalBits + 63) / 64;
            _bitCount = ulongCount * 64;
            _bits = new ulong[ulongCount];

            // k = (m / n) * ln(2)
            int k = (int)Math.Max(1, Math.Round(((double)_bitCount / expectedEntries) * 0.693147180559945));
            _hashFunctions = Math.Min(k, 8); // Cap at 8 hash evaluations
        }

        public ZabBloomFilter(ulong[] bits, int hashFunctions)
        {
            _bits = bits ?? throw new ArgumentNullException(nameof(bits));
            _bitCount = (ulong)_bits.Length * 64;
            _hashFunctions = hashFunctions;
        }

        /// <summary>
        /// Adds a 64-bit key hash to the filter.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(ulong keyHash)
        {
            ulong h1 = keyHash;
            ulong h2 = (keyHash >> 32) | (keyHash << 32);

            for (int i = 0; i < _hashFunctions; i++)
            {
                ulong combined = h1 + (ulong)i * h2;
                ulong bitIndex = combined % _bitCount;
                _bits[bitIndex / 64] |= (1UL << (int)(bitIndex % 64));
            }
        }

        /// <summary>
        /// Fast non-allocating existence check.
        /// If returns false, the key is 100% guaranteed NOT in the database.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MayContain(ulong keyHash)
        {
            ulong h1 = keyHash;
            ulong h2 = (keyHash >> 32) | (keyHash << 32);

            for (int i = 0; i < _hashFunctions; i++)
            {
                ulong combined = h1 + (ulong)i * h2;
                ulong bitIndex = combined % _bitCount;
                if ((_bits[bitIndex / 64] & (1UL << (int)(bitIndex % 64))) == 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Exports raw bit-vector for persistent serialization in .zab containers.
        /// </summary>
        public ulong[] ExportBits() => (ulong[])_bits.Clone();
    }
}
