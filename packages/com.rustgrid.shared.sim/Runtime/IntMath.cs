#nullable enable
using System;

namespace Rustgrid.Shared.Sim
{
    /// <summary>Integer-only math so results are identical on every runtime (no libm).</summary>
    public static class IntMath
    {
        /// <summary>Floor of the square root of a non-negative integer.</summary>
        public static long Isqrt(long value)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (value < 2) return value;
            long x = value;
            long y = (x + 1) / 2;
            while (y < x)
            {
                x = y;
                y = (x + value / x) / 2;
            }
            return x;
        }

        /// <summary>
        /// Euclidean distance rounded half up: round(sqrt(dx*dx + dy*dy)).
        /// An integer squared distance never has an exact half-integer root, so the
        /// rounding direction is decided exactly by comparing 4*d2 with (2*floor+1)^2.
        /// </summary>
        public static int RoundedDistance(int dx, int dy)
        {
            long squared = (long)dx * dx + (long)dy * dy;
            long floor = Isqrt(squared);
            long half = 2 * floor + 1;
            return (int)(floor + (4 * squared > half * half ? 1 : 0));
        }

        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
    }
}
