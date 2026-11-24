#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Rustgrid.Shared.Sim;

namespace Rustgrid.Tactics.Core
{
    /// <summary>Parsing, validation and content hashes of level and rules files. The host reads the text.</summary>
    public static class TacticsData
    {
        public const int LevelSchemaVersion = 1;
        public const int RulesSchemaVersion = 1;
        public const int MaxBoardSize = 64;

        public static LevelDefinition ReadLevel(string json) => Read<LevelDefinition>(json, "level");

        public static GameRules ReadRules(string json) => Read<GameRules>(json, "rules");

        /// <summary>
        /// Content hash of everything the core reads to play the level. Metadata
        /// (schema_version, level_id, level_index, spec) is excluded, so renaming a level
        /// or editing its spec keeps the measurements taken for it.
        /// </summary>
        public static ulong LevelHash(LevelDefinition level)
        {
            var h = new StableHasher();
            h.Add("tactics.level");
            h.Add(level.Rules.MaxTurns);
            h.Add(level.Rules.FirstSide);
            var rows = level.Board.Rows;
            h.Add(rows.Count);
            for (int y = 0; y < rows.Count; y++) h.Add(rows[y]);
            var units = SortedUnits(level.Board.Units);
            h.Add(units.Length);
            foreach (var u in units)
            {
                h.Add(u.Id);
                h.Add(u.Side);
                h.Add(u.X);
                h.Add(u.Y);
                h.Add(u.Hp);
                h.Add(u.Ap);
                h.Add(u.Weapon);
            }
            return h.Value;
        }

        /// <summary>Content hash of the game-wide rules (schema_version is metadata).</summary>
        public static ulong RulesHash(GameRules rules)
        {
            var h = new StableHasher();
            h.Add("tactics.rules");
            h.Add(rules.Determinism);
            h.Add(rules.StepAp);
            h.Add(rules.Weapons.Count);
            foreach (var w in rules.Weapons)
            {
                h.Add(w.Id);
                h.Add(w.Ap);
                h.Add(w.Range);
                h.Add(w.OptimalRange);
                h.Add(w.Accuracy);
                h.Add(w.AccuracyFalloff);
                h.Add(w.DamageMin);
                h.Add(w.DamageMax);
                h.Add(w.Projectiles);
                h.Add(w.CriticalChance);
            }
            return h.Value;
        }

        public static void ValidateRules(GameRules rules)
        {
            Require(rules.SchemaVersion == RulesSchemaVersion, $"rules schema_version must be {RulesSchemaVersion}");
            Require(rules.Determinism == "cross-device", "determinism must be 'cross-device'");
            Require(rules.StepAp >= 1 && rules.StepAp <= 255, "step_ap must be in 1..255");
            Require(rules.Weapons.Count > 0, "rules need at least one weapon");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var w in rules.Weapons)
            {
                string at = $"weapon '{w.Id}'";
                Require(w.Id.Length > 0, "weapon id must not be empty");
                Require(ids.Add(w.Id), $"{at} is defined twice");
                Require(w.Ap >= 1 && w.Ap <= 255, $"{at}: ap must be in 1..255");
                Require(w.Range >= 1 && w.Range <= MaxBoardSize * 2, $"{at}: range out of bounds");
                Require(w.OptimalRange >= 0 && w.OptimalRange <= w.Range, $"{at}: optimal_range must be in 0..range");
                Require(w.Accuracy >= 0 && w.Accuracy <= 100, $"{at}: accuracy must be a percent");
                Require(w.AccuracyFalloff >= 0 && w.AccuracyFalloff <= 100, $"{at}: accuracy_falloff must be a percent");
                Require(w.DamageMin >= 0 && w.DamageMin <= w.DamageMax && w.DamageMax <= 10000, $"{at}: damage range invalid");
                Require(w.Projectiles >= 1 && w.Projectiles <= 20, $"{at}: projectiles must be in 1..20");
                Require(w.CriticalChance >= 0 && w.CriticalChance <= 100, $"{at}: critical_chance must be a percent");
            }
        }

        public static void ValidateLevel(LevelDefinition level, GameRules rules)
        {
            string at = $"level '{level.LevelId}'";
            Require(level.SchemaVersion == LevelSchemaVersion, $"{at}: schema_version must be {LevelSchemaVersion}");
            Require(IsSlug(level.LevelId), $"{at}: level_id must match ^[a-z0-9_]+$");
            Require(level.Rules.MaxTurns >= 1 && level.Rules.MaxTurns <= 999, $"{at}: max_turns must be in 1..999");
            var target = level.Spec.TargetSide0WinPercent;
            Require(target == null || (target.Count == 2 && 0 <= target[0] && target[0] <= target[1] && target[1] <= 100),
                $"{at}: target_side0_win_percent must be [min, max] percents");

            var rows = level.Board.Rows;
            Require(rows.Count >= 1 && rows.Count <= MaxBoardSize, $"{at}: board height must be in 1..{MaxBoardSize}");
            int width = rows[0].Length;
            Require(width >= 1 && width <= MaxBoardSize, $"{at}: board width must be in 1..{MaxBoardSize}");
            for (int y = 0; y < rows.Count; y++)
            {
                Require(rows[y].Length == width, $"{at}: row {y} has length {rows[y].Length}, expected {width}");
                foreach (char c in rows[y]) Require(c == '.' || c == '#', $"{at}: row {y} has unknown cell '{c}'");
            }

            var weapons = new HashSet<string>(StringComparer.Ordinal);
            foreach (var w in rules.Weapons) weapons.Add(w.Id);
            var ids = new HashSet<int>();
            var cells = new HashSet<int>();
            int maxSide = -1;
            foreach (var u in level.Board.Units)
            {
                string unit = $"{at}: unit {u.Id}";
                Require(u.Id >= 1, $"{unit}: id must be positive");
                Require(ids.Add(u.Id), $"{unit}: duplicate id");
                Require(u.Side >= 0 && u.Side < 8, $"{unit}: side must be in 0..7");
                Require(u.X >= 0 && u.X < width && u.Y >= 0 && u.Y < rows.Count, $"{unit}: outside the board");
                Require(rows[u.Y][u.X] == '.', $"{unit}: placed on a wall");
                Require(cells.Add(u.Y * width + u.X), $"{unit}: cell already occupied");
                Require(u.Hp >= 1 && u.Hp <= 9999, $"{unit}: hp must be in 1..9999");
                Require(u.Ap >= 1 && u.Ap <= 255, $"{unit}: ap must be in 1..255");
                Require(weapons.Contains(u.Weapon), $"{unit}: unknown weapon '{u.Weapon}'");
                if (u.Side > maxSide) maxSide = u.Side;
            }
            Require(maxSide >= 1, $"{at}: a battle needs at least two sides");
            for (int side = 0; side <= maxSide; side++)
            {
                bool present = false;
                foreach (var u in level.Board.Units) present |= u.Side == side;
                Require(present, $"{at}: sides must be numbered 0..n-1 without gaps (side {side} has no units)");
            }
            Require(level.Rules.FirstSide >= 0 && level.Rules.FirstSide <= maxSide, $"{at}: first_side has no units");
        }

        internal static UnitSpawn[] SortedUnits(List<UnitSpawn> units)
        {
            var sorted = units.ToArray();
            Array.Sort(sorted, (a, b) => a.Id.CompareTo(b.Id));
            return sorted;
        }

        private static T Read<T>(string json, string what) where T : class
        {
            var settings = new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error,
            };
            try
            {
                return JsonConvert.DeserializeObject<T>(json, settings)
                       ?? throw new DefinitionException($"{what} file is empty");
            }
            catch (JsonException e)
            {
                throw new DefinitionException($"{what} file is not valid: {e.Message}", e);
            }
        }

        private static bool IsSlug(string id)
        {
            if (id.Length == 0) return false;
            foreach (char c in id)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')) return false;
            return true;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new DefinitionException(message);
        }
    }
}
