#nullable enable
using Rustgrid.Shared.Sim;

namespace Rustgrid.Tactics.Core
{
    public readonly struct BurstResult
    {
        public BurstResult(int hits, int damage, bool critical)
        {
            Hits = hits;
            Damage = damage;
            Critical = critical;
        }

        public int Hits { get; }
        public int Damage { get; }
        public bool Critical { get; }
    }

    /// <summary>Pure combat formulas. Integers only (cross-device determinism).</summary>
    public static class Combat
    {
        public const int MinHitChance = 5;
        public const int MaxHitChance = 95;

        public static int Distance(int x0, int y0, int x1, int y1) => IntMath.RoundedDistance(x1 - x0, y1 - y0);

        /// <summary>Per-projectile hit chance in percent at the given distance.</summary>
        public static int HitChance(Weapon weapon, int distance)
        {
            int beyond = distance > weapon.OptimalRange ? distance - weapon.OptimalRange : 0;
            return IntMath.Clamp(weapon.Accuracy - beyond * weapon.AccuracyFalloff, MinHitChance, MaxHitChance);
        }

        /// <summary>
        /// Each projectile rolls its own hit. A landed projectile rolls the full-burst damage and
        /// deals its integer share of it, so a burst where everything lands stays within
        /// [damage_min, damage_max] give or take projectiles - 1. A critical projectile deals
        /// double its share.
        /// </summary>
        public static BurstResult Burst(ref Rng rng, Weapon weapon, int hitChance)
        {
            int total = 0;
            int hits = 0;
            bool critical = false;
            int n = weapon.Projectiles;
            for (int i = 0; i < n; i++)
            {
                if (rng.NextInt(100) >= hitChance) continue;
                hits++;
                int full = rng.NextInt(weapon.DamageMin, weapon.DamageMax + 1);
                int share = full * (i + 1) / n - full * i / n;
                bool crit = share > 0 && weapon.CriticalChance > 0 && rng.NextInt(100) < weapon.CriticalChance;
                total += crit ? share * 2 : share;
                critical |= crit;
            }
            return new BurstResult(hits, total, critical);
        }

        /// <summary>
        /// Walls between the two cells block sight; units do not. The line is always traced
        /// from the lower cell to the higher one, so sight is symmetric for both fighters.
        /// </summary>
        public static bool HasLineOfSight(Board board, int x0, int y0, int x1, int y1)
        {
            if (y1 < y0 || (y1 == y0 && x1 < x0))
            {
                (x0, x1) = (x1, x0);
                (y0, y1) = (y1, y0);
            }
            int dx = x1 > x0 ? x1 - x0 : x0 - x1;
            int dy = y1 > y0 ? y1 - y0 : y0 - y1;
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            int x = x0;
            int y = y0;
            while (x != x1 || y != y1)
            {
                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y += sy;
                }
                if ((x != x1 || y != y1) && board.IsWall(x, y)) return false;
            }
            return true;
        }
    }
}
