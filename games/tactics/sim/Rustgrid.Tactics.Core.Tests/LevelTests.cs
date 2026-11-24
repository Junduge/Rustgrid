using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Rustgrid.Shared.Sim;
using Rustgrid.Tactics.Agents;

namespace Rustgrid.Tactics.Core.Tests
{
    /// <summary>Pinned values. An intentional change to hashing, RNG or rules updates them in the same commit.</summary>
    internal static class Golden
    {
        public const string Arena01LevelHash = "bc86c610435e1193";
        public const string RulesHash = "c6df0fbd5b77fbfe";
        public const string Arena01Seed42GreedyFinalHash = "287d05e152740285";
    }

    public class LevelHashTests
    {
        [Test]
        public void MetadataEditsKeepTheHash()
        {
            ulong original = TacticsData.LevelHash(TestData.Level("arena_01"));
            var edited = TestData.Level("arena_01");
            edited.LevelId = "renamed_map";
            edited.LevelIndex = 99;
            edited.SchemaVersion = 1;
            edited.Spec.Label = "hard";
            edited.Spec.Notes = "different notes";
            edited.Spec.TargetSide0WinPercent = new List<int> { 10, 20 };
            edited.Board.Units.Reverse();
            Assert.That(TacticsData.LevelHash(edited), Is.EqualTo(original));
        }

        [Test]
        public void EveryGameplayEditChangesTheHash()
        {
            ulong original = TacticsData.LevelHash(TestData.Level("arena_01"));
            var edits = new (string what, System.Action<LevelDefinition> edit)[]
            {
                ("max_turns", l => l.Rules.MaxTurns++),
                ("first_side", l => l.Rules.FirstSide = 1),
                ("wall", l => l.Board.Rows[0] = "#" + l.Board.Rows[0].Substring(1)),
                ("hp", l => l.Board.Units[0].Hp--),
                ("ap", l => l.Board.Units[0].Ap++),
                ("x", l => l.Board.Units[0].X++),
                ("side", l => l.Board.Units[0].Side = 1),
                ("weapon", l => l.Board.Units[0].Weapon = "pistol"),
            };
            foreach (var (what, edit) in edits)
            {
                var level = TestData.Level("arena_01");
                edit(level);
                Assert.That(TacticsData.LevelHash(level), Is.Not.EqualTo(original), what);
            }
        }

        [Test]
        public void HashesArePinned()
        {
            Assert.That(StableHasher.ToHex(TacticsData.LevelHash(TestData.Level("arena_01"))), Is.EqualTo(Golden.Arena01LevelHash));
            Assert.That(StableHasher.ToHex(TacticsData.RulesHash(TestData.Rules())), Is.EqualTo(Golden.RulesHash));
        }
    }

    /// <summary>Every level file is covered automatically: add a file, get the checks.</summary>
    public class LevelFileTests
    {
        private static IEnumerable<string> Levels() => TestData.LevelIds();

        [Test]
        public void RulesFileIsValid() => Assert.DoesNotThrow(() => TacticsData.ValidateRules(TestData.Rules()));

        [TestCaseSource(nameof(Levels))]
        public void LevelIsValidAndIdMatchesFileName(string levelId)
        {
            var level = TestData.Level(levelId);
            Assert.That(level.LevelId, Is.EqualTo(levelId));
            Assert.DoesNotThrow(() => TacticsData.ValidateLevel(level, TestData.Rules()));
        }

        [TestCaseSource(nameof(Levels))]
        public void GreedyBotsCanFinishTheBattle(string levelId)
        {
            var level = TestData.Level(levelId);
            var results = Enumerable.Range(1, 40)
                .Select(s => Episode.Play(level, TestData.Rules(), (ulong)s, DeterminismTests.Agents(("greedy", "greedy"))))
                .ToList();
            Assert.That(results.Count(r => r.Outcome == Outcome.Victory), Is.GreaterThan(0), "greedy bots never reach a decision: the map may be a stalemate");
        }

        [Test]
        public void UnknownFieldsAreRejected()
        {
            var ex = Assert.Throws<DefinitionException>(() => TacticsData.ReadRules("{\"schema_version\":1,\"determinism\":\"cross-device\",\"step_ap\":2,\"weapons\":[],\"stepap\":3}"));
            Assert.That(ex!.Message, Does.Contain("stepap"));
        }

        [Test]
        public void InconsistentLevelsAreRejected()
        {
            var rules = TestData.Rules();
            var onWall = TestData.Duel(new[] { "#.", ".." }, (1, 0, 0, 0, "pistol"), (2, 1, 1, 1, "pistol"));
            Assert.Throws<DefinitionException>(() => TacticsData.ValidateLevel(onWall, rules));
            var oneSide = TestData.Duel(new[] { "..", ".." }, (1, 0, 0, 0, "pistol"), (2, 0, 1, 1, "pistol"));
            Assert.Throws<DefinitionException>(() => TacticsData.ValidateLevel(oneSide, rules));
            var badWeapon = TestData.Duel(new[] { "..", ".." }, (1, 0, 0, 0, "laser"), (2, 1, 1, 1, "pistol"));
            Assert.Throws<DefinitionException>(() => TacticsData.ValidateLevel(badWeapon, rules));
            var ragged = TestData.Duel(new[] { "...", ".." }, (1, 0, 0, 0, "pistol"), (2, 1, 1, 1, "pistol"));
            Assert.Throws<DefinitionException>(() => TacticsData.ValidateLevel(ragged, rules));
        }
    }
}
