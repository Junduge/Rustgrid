using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Rustgrid.Shared.Sim;

namespace Rustgrid.Tactics.Core.Tests
{
    public class RulesTests
    {
        private static readonly string[] OpenField =
        {
            "..........",
            "..........",
            "..........",
            "..........",
            "..........",
        };

        [TestCase(1, 1, 1)]   // sqrt 2 = 1.41
        [TestCase(2, 1, 2)]   // sqrt 5 = 2.24
        [TestCase(2, 2, 3)]   // sqrt 8 = 2.83
        [TestCase(3, 3, 4)]   // sqrt 18 = 4.24
        [TestCase(4, 3, 5)]
        [TestCase(0, 7, 7)]
        [TestCase(-6, 2, 6)]  // sqrt 40 = 6.32
        public void DistanceIsEuclideanRoundedHalfUp(int dx, int dy, int expected)
        {
            Assert.That(IntMath.RoundedDistance(dx, dy), Is.EqualTo(expected));
        }

        [Test]
        public void HitChanceFallsOffBeyondOptimalRangeAndIsClamped()
        {
            var rifle = Weapon("rifle"); // accuracy 80, optimal 9, falloff 4
            Assert.That(Combat.HitChance(rifle, 3), Is.EqualTo(80));
            Assert.That(Combat.HitChance(rifle, 9), Is.EqualTo(80));
            Assert.That(Combat.HitChance(rifle, 12), Is.EqualTo(68));
            Assert.That(Combat.HitChance(rifle, 40), Is.EqualTo(Combat.MinHitChance));
        }

        [Test]
        public void FullBurstWithoutCriticalsStaysWithinWeaponDamage()
        {
            var shotgun = Weapon("shotgun"); // 6 projectiles, 40..60
            for (ulong seed = 1; seed <= 500; seed++)
            {
                var rng = new Rng(seed);
                var burst = Combat.Burst(ref rng, shotgun, 100);
                Assert.That(burst.Hits, Is.EqualTo(shotgun.Projectiles));
                if (burst.Critical) continue;
                // Each projectile rolls its own full damage, so integer shares can drift by < 1 each.
                int slack = shotgun.Projectiles - 1;
                Assert.That(burst.Damage, Is.InRange(shotgun.DamageMin - slack, shotgun.DamageMax + slack), $"seed {seed}");
            }
        }

        [Test]
        public void BurstThatMissesEverythingDealsNoDamage()
        {
            var rng = new Rng(7);
            var burst = Combat.Burst(ref rng, Weapon("smg"), 0);
            Assert.That((burst.Hits, burst.Damage, burst.Critical), Is.EqualTo((0, 0, false)));
        }

        [Test]
        public void WallsBlockSightAndSightIsSymmetric()
        {
            var game = Game.Create(TestData.Duel(new[]
            {
                ".......",
                "...#...",
                "...#...",
                ".......",
            }, (1, 0, 1, 1, "rifle"), (2, 1, 5, 2, "rifle")), TestData.Rules(), 1);
            var board = game.State.Board;
            Assert.That(Combat.HasLineOfSight(board, 1, 1, 5, 2), Is.False);
            Assert.That(Combat.HasLineOfSight(board, 1, 0, 5, 0), Is.True);
            for (int a = 0; a < board.Width * board.Height; a++)
            for (int b = 0; b < board.Width * board.Height; b++)
            {
                int x0 = a % board.Width, y0 = a / board.Width, x1 = b % board.Width, y1 = b / board.Width;
                Assert.That(Combat.HasLineOfSight(board, x0, y0, x1, y1), Is.EqualTo(Combat.HasLineOfSight(board, x1, y1, x0, y0)),
                    $"({x0},{y0})-({x1},{y1})");
            }
        }

        [Test]
        public void StepCostsActionPointsAndCannotEnterWallsOrCutCorners()
        {
            var game = Game.Create(TestData.Duel(new[]
            {
                ".....",
                ".#...",
                ".....",
            }, (1, 0, 0, 0, "pistol"), (2, 1, 4, 2, "pistol")), TestData.Rules(), 1);

            Assert.Throws<InvalidCommandException>(() => game.Apply(new MoveCommand(1, 1, 1)), "wall");
            game.Apply(new MoveCommand(1, 1, 0));
            Assert.That(game.State.Units[0].Ap, Is.EqualTo(20 - TestData.Rules().StepAp));
            // From (1,0) to (2,1) is fine; from (0,0)... a diagonal past the wall corner is not.
            var corner = Game.Create(TestData.Duel(new[] { "..", "#." }, (1, 0, 0, 0, "pistol"), (2, 1, 1, 1, "pistol")), TestData.Rules(), 1);
            Assert.Throws<InvalidCommandException>(() => corner.Apply(new MoveCommand(1, 1, 1)));
        }

        [Test]
        public void IllegalCommandLeavesStateUntouched()
        {
            var wide = OpenField.Select(r => r + "..").ToArray();
            // Pistol range 10; the target is round(sqrt(11^2 + 4^2)) = 12 cells away.
            var game = Game.Create(TestData.Duel(wide, (1, 0, 0, 0, "pistol"), (2, 1, 11, 4, "pistol")), TestData.Rules(), 3);
            ulong before = game.StateHash();
            Assert.Throws<InvalidCommandException>(() => game.Apply(new ShootCommand(1, 2)), "out of range");
            Assert.Throws<InvalidCommandException>(() => game.Apply(new MoveCommand(2, 10, 4)), "not the active side");
            Assert.Throws<InvalidCommandException>(() => game.Apply(new MoveCommand(1, 5, 5)), "not a neighbour");
            Assert.That(game.StateHash(), Is.EqualTo(before));
        }

        [Test]
        public void CannotShootAllies()
        {
            var game = Game.Create(TestData.Duel(OpenField, (1, 0, 0, 0, "pistol"), (2, 0, 1, 0, "pistol"), (3, 1, 9, 4, "pistol")), TestData.Rules(), 3);
            Assert.That(game.LegalCommands().OfType<ShootCommand>().Any(s => s.TargetId == 2), Is.False);
            Assert.Throws<InvalidCommandException>(() => game.Apply(new ShootCommand(1, 2)));
        }

        [Test]
        public void EndingTurnsRotatesSidesRestoresApAndCountsRounds()
        {
            var game = Game.Create(TestData.Duel(OpenField, (1, 0, 0, 0, "pistol"), (2, 1, 9, 4, "pistol")), TestData.Rules(), 3);
            game.Apply(new MoveCommand(1, 1, 0));
            var result = game.Apply(new EndTurnCommand());
            Assert.That((game.State.ActiveSide, game.State.Turn), Is.EqualTo((1, 1)));
            Assert.That(game.State.Units[0].Ap, Is.EqualTo(0));
            Assert.That(game.State.Units[1].Ap, Is.EqualTo(20));
            Assert.That(result.Events.OfType<TurnStarted>().Single().Side, Is.EqualTo(1));
            game.Apply(new EndTurnCommand());
            Assert.That((game.State.ActiveSide, game.State.Turn), Is.EqualTo((0, 2)));
            Assert.That(game.State.Units[0].Ap, Is.EqualTo(20));
        }

        [Test]
        public void BattleIsDrawnAfterMaxTurns()
        {
            var level = TestData.Duel(OpenField, (1, 0, 0, 0, "pistol"), (2, 1, 9, 4, "pistol"));
            level.Rules.MaxTurns = 2;
            var game = Game.Create(level, TestData.Rules(), 3);
            StepResult last = default;
            for (int i = 0; i < 4; i++) last = game.Apply(new EndTurnCommand());
            Assert.That(game.State.Outcome, Is.EqualTo(Outcome.Draw));
            Assert.That(last.Events.OfType<BattleEnded>().Single().WinnerSide, Is.EqualTo(-1));
            Assert.Throws<InvalidCommandException>(() => game.Apply(new EndTurnCommand()));
        }

        [Test]
        public void KillingTheLastEnemyWinsTheBattle()
        {
            for (ulong seed = 1; seed <= 50; seed++)
            {
                var game = Game.Create(TestData.Duel(OpenField, (1, 0, 0, 0, "shotgun"), (2, 1, 1, 0, "pistol")), TestData.Rules(), seed);
                var events = new List<GameEvent>();
                while (!game.IsOver)
                {
                    var shot = game.LegalCommands().OfType<ShootCommand>().FirstOrDefault();
                    var command = game.State.ActiveSide == 0 && shot != null ? (Command)shot : new EndTurnCommand();
                    events.AddRange(game.Apply(command).Events);
                }
                if (game.State.Outcome == Outcome.Draw) continue;
                Assert.That(game.State.WinnerSide, Is.EqualTo(0), $"seed {seed}");
                Assert.That(events.OfType<UnitKilled>().Single().UnitId, Is.EqualTo(2));
                Assert.That(game.State.Units[1].Hp, Is.EqualTo(0));
                Assert.That(game.State.IsOccupied(1, 0), Is.False, "a dead unit frees its cell");
                return;
            }
            Assert.Fail("the shotgun never won in 50 seeds");
        }

        private static Weapon Weapon(string id) =>
            Game.Create(TestData.Duel(OpenField, (1, 0, 0, 0, id), (2, 1, 9, 4, id)), TestData.Rules(), 1)
                .State.Rules.Weapons.Single(w => w.Id == id);
    }
}
