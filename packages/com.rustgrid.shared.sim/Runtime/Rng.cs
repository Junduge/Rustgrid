#nullable enable
// Deterministic RNG for the game core. C# 9 / netstandard2.1, no dependencies.
// xoshiro128** (Blackman & Vigna, public domain) + SplitMix64 for seed expansion.
//
// Why a custom one instead of System.Random / UnityEngine.Random:
//  - seeded System.Random uses a "legacy" algorithm that Microsoft keeps "for compatibility"
//    and does not promise identical sequences across .NET and Mono versions;
//  - UnityEngine.Random is global static state shared with particles and animation;
//  - this type is a value type inside the game state: it is cloned with it and hashed with it,
//    and hundreds of Game instances in one process don't interfere with each other.
//
// Usage rules:
//  - store it as a field in GameState and pass it by ref; never make it static;
//  - give each subsystem/agent its own Fork(), so an extra call in one place doesn't shift everything else;
//  - print the seed in every log, every replay and every failed-test message.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Rustgrid.Shared.Sim
{
    public struct Rng : IEquatable<Rng>
    {
        private uint _s0, _s1, _s2, _s3;

        public Rng(ulong seed)
        {
            ulong x = seed;
            ulong a = SplitMix64(ref x);
            ulong b = SplitMix64(ref x);
            _s0 = (uint)a;
            _s1 = (uint)(a >> 32);
            _s2 = (uint)b;
            _s3 = (uint)(b >> 32);
            if ((_s0 | _s1 | _s2 | _s3) == 0) _s3 = 1; // xoshiro state must never be all zeros
        }

        private static ulong SplitMix64(ref ulong x)
        {
            ulong z = (x += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Rotl(uint x, int k) => (x << k) | (x >> (32 - k));

        /// <summary>The next 32 random bits.</summary>
        public uint NextUInt()
        {
            uint result = Rotl(_s1 * 5, 7) * 9;
            uint t = _s1 << 9;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = Rotl(_s3, 11);
            return result;
        }

        /// <summary>Uniform integer in [0, maxExclusive). Rejection sampling: no modulo bias, identical on every runtime.</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            uint range = (uint)maxExclusive;
            uint threshold = (uint)((0x1_0000_0000UL - range) % range);
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold) return (int)(r % range);
            }
        }

        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        public int NextInt(int minInclusive, int maxExclusive) => minInclusive + NextInt(maxExclusive - minInclusive);

        /// <summary>True with probability permille/1000. Probabilities in the core are stored as integers to keep float out.</summary>
        public bool Chance(int permille) => NextInt(1000) < permille;

        /// <summary>Picks an index by integer weights (e.g., a spawn table). The sum of the weights must be > 0.</summary>
        public int WeightedIndex(IReadOnlyList<int> weights)
        {
            int total = 0;
            for (int i = 0; i < weights.Count; i++) total += weights[i];
            int roll = NextInt(total);
            for (int i = 0; i < weights.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0) return i;
            }
            return weights.Count - 1;
        }

        /// <summary>In-place Fisher–Yates; the iteration order is deterministic.</summary>
        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = NextInt(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        /// <summary>An independent stream derived from the current one. For subsystems and agents.</summary>
        public Rng Fork()
        {
            ulong hi = NextUInt();
            ulong lo = NextUInt();
            return new Rng((hi << 32) | lo);
        }

        /// <summary>The RNG's contribution to the game state hash.</summary>
        public ulong StateHash()
        {
            ulong a = ((ulong)_s0 << 32) | _s1;
            ulong b = ((ulong)_s2 << 32) | _s3;
            return a ^ (b * 0x9E3779B97F4A7C15UL);
        }

        public bool Equals(Rng other) => _s0 == other._s0 && _s1 == other._s1 && _s2 == other._s2 && _s3 == other._s3;
        public override bool Equals(object? obj) => obj is Rng other && Equals(other);
        public override int GetHashCode() => (int)(_s0 ^ (_s1 * 31u) ^ (_s2 * 131u) ^ (_s3 * 1313u));
        public override string ToString() => $"Rng({_s0:x8}{_s1:x8}{_s2:x8}{_s3:x8})";
    }
}
