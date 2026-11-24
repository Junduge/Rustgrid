#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Rustgrid.Tactics.Core
{
    public enum Outcome
    {
        None = 0,
        Victory = 1,
        Draw = 2,
    }

    /// <summary>Immutable map geometry, shared by all clones of a game.</summary>
    public sealed class Board
    {
        private readonly bool[] _walls;

        internal Board(BoardDefinition definition)
        {
            Height = definition.Rows.Count;
            Width = definition.Rows[0].Length;
            _walls = new bool[Width * Height];
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                _walls[y * Width + x] = definition.Rows[y][x] == '#';
        }

        public int Width { get; }
        public int Height { get; }

        public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        /// <summary>Cells outside the board count as walls.</summary>
        public bool IsWall(int x, int y) => !Inside(x, y) || _walls[y * Width + x];
    }

    /// <summary>Immutable weapon stats copied from the rules file.</summary>
    public sealed class Weapon
    {
        internal Weapon(WeaponDefinition d)
        {
            Id = d.Id;
            Ap = d.Ap;
            Range = d.Range;
            OptimalRange = d.OptimalRange;
            Accuracy = d.Accuracy;
            AccuracyFalloff = d.AccuracyFalloff;
            DamageMin = d.DamageMin;
            DamageMax = d.DamageMax;
            Projectiles = d.Projectiles;
            CriticalChance = d.CriticalChance;
        }

        public string Id { get; }
        public int Ap { get; }
        public int Range { get; }
        public int OptimalRange { get; }
        public int Accuracy { get; }
        public int AccuracyFalloff { get; }
        public int DamageMin { get; }
        public int DamageMax { get; }
        public int Projectiles { get; }
        public int CriticalChance { get; }
    }

    /// <summary>The effective rules of one battle: game rules plus the level's rules.</summary>
    public sealed class RuleSet
    {
        internal RuleSet(GameRules rules, LevelDefinition level, int sideCount)
        {
            StepAp = rules.StepAp;
            MaxTurns = level.Rules.MaxTurns;
            FirstSide = level.Rules.FirstSide;
            SideCount = sideCount;
            var weapons = new Weapon[rules.Weapons.Count];
            for (int i = 0; i < weapons.Length; i++) weapons[i] = new Weapon(rules.Weapons[i]);
            Weapons = Array.AsReadOnly(weapons);
            LevelHash = TacticsData.LevelHash(level);
            RulesHash = TacticsData.RulesHash(rules);
        }

        public int StepAp { get; }
        public int MaxTurns { get; }
        public int FirstSide { get; }
        public int SideCount { get; }
        public ReadOnlyCollection<Weapon> Weapons { get; }
        public ulong LevelHash { get; }
        public ulong RulesHash { get; }

        internal int WeaponIndex(string id)
        {
            for (int i = 0; i < Weapons.Count; i++)
                if (string.Equals(Weapons[i].Id, id, StringComparison.Ordinal)) return i;
            throw new DefinitionException($"unknown weapon '{id}'");
        }
    }

    /// <summary>One fighter. A dead unit keeps its slot with Hp 0 and no longer occupies its cell.</summary>
    public readonly struct UnitState
    {
        public UnitState(int id, int side, int x, int y, int hp, int maxHp, int ap, int maxAp, int weapon)
        {
            Id = id;
            Side = side;
            X = x;
            Y = y;
            Hp = hp;
            MaxHp = maxHp;
            Ap = ap;
            MaxAp = maxAp;
            Weapon = weapon;
        }

        public int Id { get; }
        public int Side { get; }
        public int X { get; }
        public int Y { get; }
        public int Hp { get; }
        public int MaxHp { get; }
        public int Ap { get; }
        public int MaxAp { get; }
        /// <summary>Index into RuleSet.Weapons.</summary>
        public int Weapon { get; }
        public bool IsAlive => Hp > 0;

        internal UnitState WithPosition(int x, int y, int ap) => new UnitState(Id, Side, x, y, Hp, MaxHp, ap, MaxAp, Weapon);
        internal UnitState WithAp(int ap) => new UnitState(Id, Side, X, Y, Hp, MaxHp, ap, MaxAp, Weapon);
        internal UnitState WithHp(int hp) => new UnitState(Id, Side, X, Y, hp, MaxHp, Ap, MaxAp, Weapon);
    }

    /// <summary>Read-only view of a battle. Only Game.Apply changes it.</summary>
    public sealed class GameState
    {
        private readonly UnitState[] _units;

        internal GameState(Board board, RuleSet rules, UnitState[] units, ulong seed)
        {
            Board = board;
            Rules = rules;
            _units = units;
            Units = Array.AsReadOnly(_units);
            Seed = seed;
            WinnerSide = -1;
        }

        public Board Board { get; }
        public RuleSet Rules { get; }
        /// <summary>All units sorted by id, including dead ones.</summary>
        public ReadOnlyCollection<UnitState> Units { get; }
        public ulong Seed { get; }
        public int ActiveSide { get; internal set; }
        /// <summary>Round number, starting at 1; a round ends when every living side has moved.</summary>
        public int Turn { get; internal set; }
        /// <summary>Number of commands applied so far.</summary>
        public int Tick { get; internal set; }
        public Outcome Outcome { get; internal set; }
        /// <summary>The winning side for Victory, otherwise -1.</summary>
        public int WinnerSide { get; internal set; }

        /// <summary>The battle RNG. A field so the core can pass it by ref.</summary>
        internal Shared.Sim.Rng Rng;

        internal UnitState[] MutableUnits => _units;

        public int IndexOfUnit(int id)
        {
            for (int i = 0; i < _units.Length; i++)
                if (_units[i].Id == id) return i;
            return -1;
        }

        /// <summary>Index of the living unit standing on the cell, or -1.</summary>
        public int UnitIndexAt(int x, int y)
        {
            for (int i = 0; i < _units.Length; i++)
                if (_units[i].IsAlive && _units[i].X == x && _units[i].Y == y) return i;
            return -1;
        }

        public bool IsOccupied(int x, int y) => UnitIndexAt(x, y) >= 0;

        public int LivingUnits(int side)
        {
            int count = 0;
            foreach (var u in _units)
                if (u.Side == side && u.IsAlive) count++;
            return count;
        }

        internal GameState Clone()
        {
            var copy = new GameState(Board, Rules, (UnitState[])_units.Clone(), Seed)
            {
                ActiveSide = ActiveSide,
                Turn = Turn,
                Tick = Tick,
                Outcome = Outcome,
                WinnerSide = WinnerSide,
            };
            copy.Rng = Rng;
            return copy;
        }
    }

    public abstract class Command
    {
    }

    /// <summary>Step to one of the eight neighbouring cells.</summary>
    public sealed class MoveCommand : Command
    {
        public MoveCommand(int unitId, int x, int y)
        {
            UnitId = unitId;
            X = x;
            Y = y;
        }

        public int UnitId { get; }
        public int X { get; }
        public int Y { get; }
        public override string ToString() => $"move {UnitId} -> ({X},{Y})";
    }

    public sealed class ShootCommand : Command
    {
        public ShootCommand(int unitId, int targetId)
        {
            UnitId = unitId;
            TargetId = targetId;
        }

        public int UnitId { get; }
        public int TargetId { get; }
        public override string ToString() => $"shoot {UnitId} -> {TargetId}";
    }

    public sealed class EndTurnCommand : Command
    {
        public override string ToString() => "end turn";
    }

    public abstract class GameEvent
    {
        protected GameEvent(int tick) => Tick = tick;
        public int Tick { get; }
    }

    public sealed class TurnStarted : GameEvent
    {
        public TurnStarted(int tick, int side, int turn) : base(tick)
        {
            Side = side;
            Turn = turn;
        }

        public int Side { get; }
        public int Turn { get; }
    }

    public sealed class UnitMoved : GameEvent
    {
        public UnitMoved(int tick, int unitId, int fromX, int fromY, int toX, int toY, int apLeft) : base(tick)
        {
            UnitId = unitId;
            FromX = fromX;
            FromY = fromY;
            ToX = toX;
            ToY = toY;
            ApLeft = apLeft;
        }

        public int UnitId { get; }
        public int FromX { get; }
        public int FromY { get; }
        public int ToX { get; }
        public int ToY { get; }
        public int ApLeft { get; }
    }

    public sealed class ShotFired : GameEvent
    {
        public ShotFired(int tick, int unitId, int targetId, int hitChance, int hits, int damage, bool critical, int apLeft) : base(tick)
        {
            UnitId = unitId;
            TargetId = targetId;
            HitChance = hitChance;
            Hits = hits;
            Damage = damage;
            Critical = critical;
            ApLeft = apLeft;
        }

        public int UnitId { get; }
        public int TargetId { get; }
        public int HitChance { get; }
        public int Hits { get; }
        /// <summary>Damage actually applied (capped by the target's HP).</summary>
        public int Damage { get; }
        public bool Critical { get; }
        public int ApLeft { get; }
    }

    public sealed class UnitKilled : GameEvent
    {
        public UnitKilled(int tick, int unitId, int killerId) : base(tick)
        {
            UnitId = unitId;
            KillerId = killerId;
        }

        public int UnitId { get; }
        public int KillerId { get; }
    }

    public sealed class BattleEnded : GameEvent
    {
        public BattleEnded(int tick, Outcome outcome, int winnerSide) : base(tick)
        {
            Outcome = outcome;
            WinnerSide = winnerSide;
        }

        public Outcome Outcome { get; }
        public int WinnerSide { get; }
    }

    public readonly struct StepResult
    {
        public StepResult(IReadOnlyList<GameEvent> events, Outcome outcome, int tick)
        {
            Events = events;
            Outcome = outcome;
            Tick = tick;
        }

        public IReadOnlyList<GameEvent> Events { get; }
        public Outcome Outcome { get; }
        public int Tick { get; }
    }
}
