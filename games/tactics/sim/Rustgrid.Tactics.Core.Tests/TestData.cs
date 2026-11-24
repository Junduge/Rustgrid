using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rustgrid.Tactics.Core;

namespace Rustgrid.Tactics.Core.Tests
{
    /// <summary>Locates games/tactics from the test binary and builds small in-memory battles.</summary>
    internal static class TestData
    {
        public static string GameRoot { get; } = FindGameRoot();

        public static string LevelsDir => Path.Combine(GameRoot, "levels");

        public static GameRules Rules() => TacticsData.ReadRules(File.ReadAllText(Path.Combine(GameRoot, "game.rules.json")));

        public static LevelDefinition Level(string id) => TacticsData.ReadLevel(File.ReadAllText(Path.Combine(LevelsDir, id + ".json")));

        public static IEnumerable<string> LevelIds() =>
            Directory.GetFiles(LevelsDir, "*.json")
                .Where(p => !p.EndsWith("level.schema.json", StringComparison.Ordinal))
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(id => id, StringComparer.Ordinal)!;

        /// <summary>A small battle: rows[y][x] as in level files, units as (id, side, x, y, weapon), 100 HP and 20 AP each.</summary>
        public static LevelDefinition Duel(string[] rows, params (int id, int side, int x, int y, string weapon)[] units) => new LevelDefinition
        {
            SchemaVersion = 1,
            LevelId = "test_duel",
            LevelIndex = 1,
            Spec = new LevelSpec { Label = "normal", Mechanics = new List<string> { "shoot" } },
            Rules = new LevelRules { MaxTurns = 20, FirstSide = 0 },
            Board = new BoardDefinition
            {
                Rows = rows.ToList(),
                Units = units.Select(u => new UnitSpawn { Id = u.id, Side = u.side, X = u.x, Y = u.y, Hp = 100, Ap = 20, Weapon = u.weapon }).ToList(),
            },
        };

        private static string FindGameRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "games", "tactics");
                if (File.Exists(Path.Combine(candidate, "game.rules.json"))) return candidate;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("games/tactics not found above " + AppContext.BaseDirectory);
        }
    }
}
