#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Rustgrid.Tactics.Core
{
    // Plain DTOs for the JSON files in games/tactics. The game copies what it needs into
    // immutable runtime types at Game.Create, so later edits to a DTO cannot change a battle.

    /// <summary>A battle map with its starting units. Identity is (LevelId, TacticsData.LevelHash).</summary>
    public sealed class LevelDefinition
    {
        [JsonProperty("schema_version", Required = Required.Always)] public int SchemaVersion { get; set; }
        [JsonProperty("level_id", Required = Required.Always)] public string LevelId { get; set; } = "";
        [JsonProperty("level_index")] public int LevelIndex { get; set; }
        [JsonProperty("spec", Required = Required.Always)] public LevelSpec Spec { get; set; } = new LevelSpec();
        [JsonProperty("rules", Required = Required.Always)] public LevelRules Rules { get; set; } = new LevelRules();
        [JsonProperty("board", Required = Required.Always)] public BoardDefinition Board { get; set; } = new BoardDefinition();
    }

    /// <summary>Design intent. Metadata: never part of the level hash.</summary>
    public sealed class LevelSpec
    {
        [JsonProperty("label", Required = Required.Always)] public string Label { get; set; } = "";
        [JsonProperty("mechanics", Required = Required.Always)] public List<string> Mechanics { get; set; } = new List<string>();
        /// <summary>[min, max] percent of side-0 wins when both sides use the same bot (map fairness).</summary>
        [JsonProperty("target_side0_win_percent")] public List<int>? TargetSide0WinPercent { get; set; }
        [JsonProperty("notes")] public string? Notes { get; set; }
    }

    public sealed class LevelRules
    {
        [JsonProperty("max_turns", Required = Required.Always)] public int MaxTurns { get; set; }
        [JsonProperty("first_side")] public int FirstSide { get; set; }
    }

    public sealed class BoardDefinition
    {
        /// <summary>rows[y][x]: '.' floor, '#' wall (blocks movement and sight).</summary>
        [JsonProperty("rows", Required = Required.Always)] public List<string> Rows { get; set; } = new List<string>();
        [JsonProperty("units", Required = Required.Always)] public List<UnitSpawn> Units { get; set; } = new List<UnitSpawn>();
    }

    public sealed class UnitSpawn
    {
        [JsonProperty("id", Required = Required.Always)] public int Id { get; set; }
        [JsonProperty("side", Required = Required.Always)] public int Side { get; set; }
        [JsonProperty("x", Required = Required.Always)] public int X { get; set; }
        [JsonProperty("y", Required = Required.Always)] public int Y { get; set; }
        [JsonProperty("hp", Required = Required.Always)] public int Hp { get; set; }
        /// <summary>Action points restored at the start of each of the side's turns.</summary>
        [JsonProperty("ap", Required = Required.Always)] public int Ap { get; set; }
        [JsonProperty("weapon", Required = Required.Always)] public string Weapon { get; set; } = "";
    }

    /// <summary>Game-wide rules (game.rules.json). Identity is TacticsData.RulesHash.</summary>
    public sealed class GameRules
    {
        [JsonProperty("schema_version", Required = Required.Always)] public int SchemaVersion { get; set; }
        [JsonProperty("determinism", Required = Required.Always)] public string Determinism { get; set; } = "";
        /// <summary>AP spent per step to a neighbouring cell (diagonal steps cost the same).</summary>
        [JsonProperty("step_ap", Required = Required.Always)] public int StepAp { get; set; }
        [JsonProperty("weapons", Required = Required.Always)] public List<WeaponDefinition> Weapons { get; set; } = new List<WeaponDefinition>();
    }

    public sealed class WeaponDefinition
    {
        [JsonProperty("id", Required = Required.Always)] public string Id { get; set; } = "";
        [JsonProperty("ap", Required = Required.Always)] public int Ap { get; set; }
        [JsonProperty("range", Required = Required.Always)] public int Range { get; set; }
        /// <summary>Distance up to which accuracy is not reduced.</summary>
        [JsonProperty("optimal_range", Required = Required.Always)] public int OptimalRange { get; set; }
        /// <summary>Hit chance per projectile in percent at optimal range.</summary>
        [JsonProperty("accuracy", Required = Required.Always)] public int Accuracy { get; set; }
        /// <summary>Percent of hit chance lost per cell beyond optimal range.</summary>
        [JsonProperty("accuracy_falloff", Required = Required.Always)] public int AccuracyFalloff { get; set; }
        /// <summary>Damage of a full burst: each landed projectile deals its share of a roll in [min, max].</summary>
        [JsonProperty("damage_min", Required = Required.Always)] public int DamageMin { get; set; }
        [JsonProperty("damage_max", Required = Required.Always)] public int DamageMax { get; set; }
        [JsonProperty("projectiles", Required = Required.Always)] public int Projectiles { get; set; }
        /// <summary>Percent chance that a landed projectile deals double damage.</summary>
        [JsonProperty("critical_chance", Required = Required.Always)] public int CriticalChance { get; set; }
    }
}
