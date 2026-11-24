#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Rustgrid.Shared.Sim;

namespace Rustgrid.Tactics.Core
{
    /// <summary>
    /// One battle. Sides act in turn order; during its turn a side spends its units' action
    /// points on steps and shots, then ends the turn. Apply is the only way to change state.
    /// </summary>
    public sealed class Game
    {
        private const int Directions = 8;

        private readonly GameState _state;

        private Game(GameState state) => _state = state;

        public GameState State => _state;
        public bool IsOver => _state.Outcome != Outcome.None;

        public static Game Create(LevelDefinition level, GameRules rules, ulong seed)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            TacticsData.ValidateRules(rules);
            TacticsData.ValidateLevel(level, rules);

            var spawns = TacticsData.SortedUnits(level.Board.Units);
            int sideCount = 0;
            foreach (var s in spawns) sideCount = Math.Max(sideCount, s.Side + 1);
            var ruleSet = new RuleSet(rules, level, sideCount);
            var units = new UnitState[spawns.Length];
            for (int i = 0; i < spawns.Length; i++)
            {
                var s = spawns[i];
                units[i] = new UnitState(s.Id, s.Side, s.X, s.Y, s.Hp, s.Hp, 0, s.Ap, ruleSet.WeaponIndex(s.Weapon));
            }

            var state = new GameState(new Board(level.Board), ruleSet, units, seed) { Turn = 1 };
            state.Rng = new Rng(seed);
            BeginSide(state, ruleSet.FirstSide);
            return new Game(state);
        }

        /// <summary>Every legal command in a deterministic order: per unit by id, steps, then shots; end turn last.</summary>
        public IReadOnlyList<Command> LegalCommands()
        {
            var legal = new List<Command>();
            if (IsOver) return legal;
            var units = _state.MutableUnits;
            for (int i = 0; i < units.Length; i++)
            {
                var u = units[i];
                if (!u.IsAlive || u.Side != _state.ActiveSide) continue;
                for (int d = 0; d < Directions; d++)
                {
                    int x = u.X + StepX(d);
                    int y = u.Y + StepY(d);
                    if (MoveError(u, x, y) == null) legal.Add(new MoveCommand(u.Id, x, y));
                }
                for (int t = 0; t < units.Length; t++)
                    if (ShotError(u, units[t]) == null) legal.Add(new ShootCommand(u.Id, units[t].Id));
            }
            legal.Add(new EndTurnCommand());
            return legal;
        }

        /// <summary>Applies a command or throws InvalidCommandException without changing state.</summary>
        public StepResult Apply(Command command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (IsOver) throw new InvalidCommandException("the battle is over");
            var events = new List<GameEvent>();
            int tick = _state.Tick + 1;
            switch (command)
            {
                case MoveCommand move:
                    Move(move, tick, events);
                    break;
                case ShootCommand shot:
                    Shoot(shot, tick, events);
                    break;
                case EndTurnCommand _:
                    EndTurn(tick, events);
                    break;
                default:
                    throw new InvalidCommandException($"unknown command {command.GetType().Name}");
            }
            _state.Tick = tick;
            return new StepResult(events, _state.Outcome, tick);
        }

        /// <summary>Hit chance of a legal shot in percent, or null when the shot is not legal.</summary>
        public int? ShotChance(int unitId, int targetId)
        {
            int a = _state.IndexOfUnit(unitId);
            int t = _state.IndexOfUnit(targetId);
            if (a < 0 || t < 0) return null;
            var attacker = _state.MutableUnits[a];
            var target = _state.MutableUnits[t];
            if (ShotError(attacker, target) != null) return null;
            var weapon = _state.Rules.Weapons[attacker.Weapon];
            return Combat.HitChance(weapon, Combat.Distance(attacker.X, attacker.Y, target.X, target.Y));
        }

        public ulong StateHash()
        {
            var h = new StableHasher();
            h.Add(_state.Rules.LevelHash);
            h.Add(_state.Rules.RulesHash);
            h.Add(_state.Turn);
            h.Add(_state.Tick);
            h.Add(_state.ActiveSide);
            h.Add((int)_state.Outcome);
            h.Add(_state.WinnerSide);
            var units = _state.MutableUnits;
            h.Add(units.Length);
            foreach (var u in units)
            {
                h.Add(u.Id);
                h.Add(u.Side);
                h.Add(u.X);
                h.Add(u.Y);
                h.Add(u.Hp);
                h.Add(u.MaxHp);
                h.Add(u.Ap);
                h.Add(u.MaxAp);
                h.Add(u.Weapon);
            }
            h.Add(_state.Rng.StateHash());
            return h.Value;
        }

        public Game Clone() => new Game(_state.Clone());

        /// <summary>Line-per-field dump for diffing the first divergence between two runs.</summary>
        public void Dump(TextWriter writer)
        {
            writer.WriteLine($"level_hash={StableHasher.ToHex(_state.Rules.LevelHash)} rules_hash={StableHasher.ToHex(_state.Rules.RulesHash)} seed={_state.Seed}");
            writer.WriteLine($"turn={_state.Turn} tick={_state.Tick} active_side={_state.ActiveSide} outcome={_state.Outcome} winner={_state.WinnerSide}");
            writer.WriteLine($"rng={_state.Rng}");
            foreach (var u in _state.MutableUnits)
                writer.WriteLine($"unit {u.Id} side={u.Side} pos=({u.X},{u.Y}) hp={u.Hp}/{u.MaxHp} ap={u.Ap}/{u.MaxAp} weapon={_state.Rules.Weapons[u.Weapon].Id}");
            writer.WriteLine($"hash={StableHasher.ToHex(StateHash())}");
        }

        /// <summary>Compact text for LLM agents and logs: the map with unit ids, then unit stats.</summary>
        public string ToText()
        {
            var board = _state.Board;
            var text = new StringBuilder();
            text.Append($"Turn {_state.Turn}, side {_state.ActiveSide} to act. Outcome: {_state.Outcome}.\n");
            for (int y = board.Height - 1; y >= 0; y--)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    int i = _state.UnitIndexAt(x, y);
                    text.Append(i >= 0 ? (char)('A' + _state.MutableUnits[i].Side) : board.IsWall(x, y) ? '#' : '.');
                }
                text.Append('\n');
            }
            foreach (var u in _state.MutableUnits)
            {
                if (!u.IsAlive) continue;
                text.Append($"unit {u.Id} side {(char)('A' + u.Side)} at ({u.X},{u.Y}) hp {u.Hp}/{u.MaxHp} ap {u.Ap}/{u.MaxAp} {_state.Rules.Weapons[u.Weapon].Id}\n");
            }
            return text.ToString();
        }

        private void Move(MoveCommand command, int tick, List<GameEvent> events)
        {
            int index = RequireActiveUnit(command.UnitId);
            var unit = _state.MutableUnits[index];
            string? error = MoveError(unit, command.X, command.Y);
            if (error != null) throw new InvalidCommandException(error);
            int ap = unit.Ap - _state.Rules.StepAp;
            _state.MutableUnits[index] = unit.WithPosition(command.X, command.Y, ap);
            events.Add(new UnitMoved(tick, unit.Id, unit.X, unit.Y, command.X, command.Y, ap));
        }

        private void Shoot(ShootCommand command, int tick, List<GameEvent> events)
        {
            int index = RequireActiveUnit(command.UnitId);
            int targetIndex = _state.IndexOfUnit(command.TargetId);
            if (targetIndex < 0) throw new InvalidCommandException($"unit {command.TargetId} does not exist");
            var attacker = _state.MutableUnits[index];
            var target = _state.MutableUnits[targetIndex];
            string? error = ShotError(attacker, target);
            if (error != null) throw new InvalidCommandException(error);

            var weapon = _state.Rules.Weapons[attacker.Weapon];
            int chance = Combat.HitChance(weapon, Combat.Distance(attacker.X, attacker.Y, target.X, target.Y));
            var burst = Combat.Burst(ref _state.Rng, weapon, chance);
            int damage = Math.Min(burst.Damage, target.Hp);
            int ap = attacker.Ap - weapon.Ap;
            _state.MutableUnits[index] = attacker.WithAp(ap);
            _state.MutableUnits[targetIndex] = target.WithHp(target.Hp - damage);
            events.Add(new ShotFired(tick, attacker.Id, target.Id, chance, burst.Hits, damage, burst.Critical, ap));
            if (target.Hp - damage > 0) return;

            events.Add(new UnitKilled(tick, target.Id, attacker.Id));
            int survivor = OnlyLivingSide();
            if (survivor >= 0) Finish(Outcome.Victory, survivor, tick, events);
        }

        private void EndTurn(int tick, List<GameEvent> events)
        {
            var units = _state.MutableUnits;
            for (int i = 0; i < units.Length; i++)
                if (units[i].Side == _state.ActiveSide) units[i] = units[i].WithAp(0);

            int side = _state.ActiveSide;
            int turn = _state.Turn;
            do
            {
                side++;
                if (side == _state.Rules.SideCount)
                {
                    side = 0;
                    turn++;
                }
            } while (_state.LivingUnits(side) == 0);

            if (turn > _state.Rules.MaxTurns)
            {
                Finish(Outcome.Draw, -1, tick, events);
                return;
            }
            _state.Turn = turn;
            BeginSide(_state, side);
            events.Add(new TurnStarted(tick, side, turn));
        }

        // Clockwise from north: N, NE, E, SE, S, SW, W, NW.
        private static int StepX(int direction) => direction switch { 1 => 1, 2 => 1, 3 => 1, 5 => -1, 6 => -1, 7 => -1, _ => 0 };
        private static int StepY(int direction) => direction switch { 0 => 1, 1 => 1, 3 => -1, 4 => -1, 5 => -1, 7 => 1, _ => 0 };

        private static void BeginSide(GameState state, int side)
        {
            state.ActiveSide = side;
            var units = state.MutableUnits;
            for (int i = 0; i < units.Length; i++)
                if (units[i].Side == side && units[i].IsAlive) units[i] = units[i].WithAp(units[i].MaxAp);
        }

        private void Finish(Outcome outcome, int winner, int tick, List<GameEvent> events)
        {
            _state.Outcome = outcome;
            _state.WinnerSide = winner;
            events.Add(new BattleEnded(tick, outcome, winner));
        }

        private int OnlyLivingSide()
        {
            int found = -1;
            for (int side = 0; side < _state.Rules.SideCount; side++)
            {
                if (_state.LivingUnits(side) == 0) continue;
                if (found >= 0) return -1;
                found = side;
            }
            return found;
        }

        private int RequireActiveUnit(int unitId)
        {
            int index = _state.IndexOfUnit(unitId);
            if (index < 0) throw new InvalidCommandException($"unit {unitId} does not exist");
            var unit = _state.MutableUnits[index];
            if (!unit.IsAlive) throw new InvalidCommandException($"unit {unitId} is dead");
            if (unit.Side != _state.ActiveSide) throw new InvalidCommandException($"unit {unitId} does not belong to the active side");
            return index;
        }

        private string? MoveError(UnitState unit, int x, int y)
        {
            int dx = x - unit.X;
            int dy = y - unit.Y;
            if ((dx == 0 && dy == 0) || dx < -1 || dx > 1 || dy < -1 || dy > 1) return "a step must go to a neighbouring cell";
            if (unit.Ap < _state.Rules.StepAp) return "not enough action points to move";
            var board = _state.Board;
            if (board.IsWall(x, y)) return "the cell is blocked";
            if (_state.IsOccupied(x, y)) return "the cell is occupied";
            // No corner cutting: a diagonal step needs both orthogonal neighbours open.
            if (dx != 0 && dy != 0 && (board.IsWall(unit.X + dx, unit.Y) || board.IsWall(unit.X, unit.Y + dy)))
                return "cannot cut a wall corner";
            return null;
        }

        private string? ShotError(UnitState attacker, UnitState target)
        {
            if (!target.IsAlive) return "the target is dead";
            if (target.Side == attacker.Side) return "cannot shoot an ally";
            var weapon = _state.Rules.Weapons[attacker.Weapon];
            if (attacker.Ap < weapon.Ap) return "not enough action points to shoot";
            if (Combat.Distance(attacker.X, attacker.Y, target.X, target.Y) > weapon.Range) return "the target is out of range";
            if (!Combat.HasLineOfSight(_state.Board, attacker.X, attacker.Y, target.X, target.Y)) return "no line of sight";
            return null;
        }
    }
}
