using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Rustgrid.Shared.Sim;
using Rustgrid.Tactics.Agents;

namespace Rustgrid.Tactics.Core.Tests
{
    public class DeterminismTests
    {
        private static IEnumerable<string> Levels() => TestData.LevelIds();

        [TestCaseSource(nameof(Levels))]
        public void SameSeedAndAgentsGiveTheSameBattle(string levelId)
        {
            var level = TestData.Level(levelId);
            var rules = TestData.Rules();
            foreach (var pair in new[] { ("greedy", "greedy"), ("random", "greedy") })
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var first = Episode.Play(level, rules, seed, Agents(pair));
                var second = Episode.Play(level, rules, seed, Agents(pair));
                Assert.That(second.FinalHash, Is.EqualTo(first.FinalHash), $"{levelId} {pair} seed {seed}");
            }
        }

        [Test]
        public void DifferentSeedsGiveDifferentBattles()
        {
            var level = TestData.Level("arena_01");
            var hashes = Enumerable.Range(1, 20)
                .Select(s => Episode.Play(level, TestData.Rules(), (ulong)s, Agents(("greedy", "greedy"))).FinalHash)
                .Distinct()
                .Count();
            Assert.That(hashes, Is.GreaterThan(1));
        }

        [Test]
        public void ApplyingToACloneLeavesTheOriginalUntouched()
        {
            var game = Game.Create(TestData.Level("arena_01"), TestData.Rules(), 11);
            ulong before = game.StateHash();
            var clone = game.Clone();
            Assert.That(clone.StateHash(), Is.EqualTo(before));
            var rng = new Rng(5);
            var agent = new RandomAgent();
            for (int i = 0; i < 40 && !clone.IsOver; i++) clone.Apply(agent.Choose(clone, clone.LegalCommands(), ref rng));
            Assert.That(game.StateHash(), Is.EqualTo(before));
            Assert.That(clone.StateHash(), Is.Not.EqualTo(before));
        }

        [TestCaseSource(nameof(Levels))]
        public void InvariantsHoldThroughoutRandomBattles(string levelId)
        {
            var level = TestData.Level(levelId);
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var game = Game.Create(level, TestData.Rules(), seed);
                var rng = new Rng(seed * 31);
                var agent = new RandomAgent();
                while (!game.IsOver)
                {
                    Assert.That(game.State.Tick, Is.LessThan(Episode.MaxTicks), $"{levelId} seed {seed} does not terminate");
                    var legal = game.LegalCommands();
                    Assert.That(legal.Last(), Is.TypeOf<EndTurnCommand>(), "ending the turn is always legal");
                    game.Apply(agent.Choose(game, legal, ref rng));
                    AssertInvariants(game.State, $"{levelId} seed {seed} tick {game.State.Tick}");
                }
                Assert.That(game.LegalCommands(), Is.Empty);
            }
        }

        [Test]
        public void ArenaGoldenBattleIsPinned()
        {
            // Changing rules, RNG or hashing changes this value on purpose; update it only knowingly.
            var summary = Episode.Play(TestData.Level("arena_01"), TestData.Rules(), 42, Agents(("greedy", "greedy")));
            Assert.That(StableHasher.ToHex(summary.FinalHash), Is.EqualTo(Golden.Arena01Seed42GreedyFinalHash));
        }

        private static void AssertInvariants(GameState state, string at)
        {
            var cells = new HashSet<(int, int)>();
            foreach (var u in state.Units)
            {
                Assert.That(u.Hp, Is.InRange(0, u.MaxHp), at);
                Assert.That(u.Ap, Is.InRange(0, u.MaxAp), at);
                if (!u.IsAlive) continue;
                Assert.That(state.Board.Inside(u.X, u.Y) && !state.Board.IsWall(u.X, u.Y), Is.True, at);
                Assert.That(cells.Add((u.X, u.Y)), Is.True, $"{at}: two units on ({u.X},{u.Y})");
                if (u.Side != state.ActiveSide) Assert.That(u.Ap, Is.EqualTo(0), $"{at}: idle side keeps AP");
            }
        }

        internal static IAgent[] Agents((string, string) pair) => new[] { AgentCatalog.Create(pair.Item1), AgentCatalog.Create(pair.Item2) };
    }
}
