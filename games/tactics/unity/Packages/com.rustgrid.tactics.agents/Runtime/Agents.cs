#nullable enable
using System;
using System.Collections.Generic;
using Rustgrid.Shared.Sim;
using Rustgrid.Tactics.Core;

namespace Rustgrid.Tactics.Agents
{
    /// <summary>A bot that picks one of the legal commands. Humans, scripts, search and LLMs share this seam.</summary>
    public interface IAgent
    {
        string Id { get; }
        Command Choose(Game game, IReadOnlyList<Command> legal, ref Rng rng);
    }

    /// <summary>Uniformly random legal command. The baseline every map must survive.</summary>
    public sealed class RandomAgent : IAgent
    {
        public string Id => "random";

        public Command Choose(Game game, IReadOnlyList<Command> legal, ref Rng rng) => legal[rng.NextInt(legal.Count)];
    }

    /// <summary>
    /// One-ply heuristic: take the shot with the best expected damage (finishing wounded
    /// targets first); otherwise step closer to the nearest enemy; otherwise end the turn.
    /// </summary>
    public sealed class GreedyAgent : IAgent
    {
        public string Id => "greedy";

        public Command Choose(Game game, IReadOnlyList<Command> legal, ref Rng rng)
        {
            var state = game.State;
            Command? best = null;
            long bestScore = long.MinValue;
            foreach (var command in legal)
            {
                if (!(command is ShootCommand shot)) continue;
                long score = ShotScore(game, shot);
                if (score > bestScore)
                {
                    best = shot;
                    bestScore = score;
                }
            }
            if (best != null) return best;

            int bestGain = 0;
            foreach (var command in legal)
            {
                if (!(command is MoveCommand move)) continue;
                var unit = state.Units[state.IndexOfUnit(move.UnitId)];
                int gain = NearestEnemySquared(state, unit.Side, unit.X, unit.Y)
                           - NearestEnemySquared(state, unit.Side, move.X, move.Y);
                if (gain > bestGain)
                {
                    best = move;
                    bestGain = gain;
                }
            }
            return best ?? legal[legal.Count - 1];
        }

        private static long ShotScore(Game game, ShootCommand shot)
        {
            var state = game.State;
            var attacker = state.Units[state.IndexOfUnit(shot.UnitId)];
            var target = state.Units[state.IndexOfUnit(shot.TargetId)];
            var weapon = state.Rules.Weapons[attacker.Weapon];
            int chance = game.ShotChance(shot.UnitId, shot.TargetId) ?? 0;
            // Expected damage in thousandths; overkill is worthless, so cap at the target's HP.
            long expected = (long)chance * (weapon.DamageMin + weapon.DamageMax) * (100 + weapon.CriticalChance) / 20;
            long capped = Math.Min(expected, target.Hp * 1000L);
            return capped * 10000 / target.Hp;
        }

        private static int NearestEnemySquared(GameState state, int side, int x, int y)
        {
            int best = int.MaxValue;
            foreach (var u in state.Units)
            {
                if (!u.IsAlive || u.Side == side) continue;
                int dx = u.X - x;
                int dy = u.Y - y;
                best = Math.Min(best, dx * dx + dy * dy);
            }
            return best;
        }
    }

    public static class AgentCatalog
    {
        public static IAgent Create(string id)
        {
            switch (id)
            {
                case "random": return new RandomAgent();
                case "greedy": return new GreedyAgent();
                default: throw new ArgumentException($"unknown agent '{id}' (known: random, greedy)", nameof(id));
            }
        }
    }

    public readonly struct EpisodeSummary
    {
        public EpisodeSummary(Outcome outcome, int winnerSide, int turns, int ticks, ulong finalHash)
        {
            Outcome = outcome;
            WinnerSide = winnerSide;
            Turns = turns;
            Ticks = ticks;
            FinalHash = finalHash;
        }

        public Outcome Outcome { get; }
        public int WinnerSide { get; }
        public int Turns { get; }
        public int Ticks { get; }
        public ulong FinalHash { get; }
    }

    /// <summary>Plays one battle to the end with one agent per side. Shared by the runner, tests and Unity smoke checks.</summary>
    public static class Episode
    {
        /// <summary>Upper bound on commands per battle; exceeding it means a rules bug (a turn that never ends).</summary>
        public const int MaxTicks = 100_000;

        public static EpisodeSummary Play(LevelDefinition level, GameRules rules, ulong seed, IReadOnlyList<IAgent> sides)
        {
            var game = Game.Create(level, rules, seed);
            if (sides.Count != game.State.Rules.SideCount)
                throw new ArgumentException($"level has {game.State.Rules.SideCount} sides, got {sides.Count} agents", nameof(sides));
            // Agents get their own stream so a bot change never shifts the battle's dice.
            var agentRng = new Rng(seed ^ 0xA6E7_5EED_0000_0001UL);
            while (!game.IsOver)
            {
                if (game.State.Tick >= MaxTicks) throw new InvalidOperationException($"battle did not finish in {MaxTicks} commands (seed {seed})");
                var agent = sides[game.State.ActiveSide];
                game.Apply(agent.Choose(game, game.LegalCommands(), ref agentRng));
            }
            var s = game.State;
            return new EpisodeSummary(s.Outcome, s.WinnerSide, s.Turn, s.Tick, game.StateHash());
        }
    }
}
