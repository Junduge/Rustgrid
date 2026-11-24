# Architecture

```
dotnet                    Rustgrid.Sim.Runner, Rustgrid.Tactics.Core.Tests
                                   |
engine-free (netstandard2.1, C# 9) Rustgrid.Tactics.Agents -> Rustgrid.Tactics.Core -> Rustgrid.Shared.Sim
                                   |
Unity                     Rustgrid.Tactics.Composition -> Presentation -> Core
                          Rustgrid.Tactics.Authoring (editor only)
```

Dependencies point toward the core. The same `.cs` files are compiled by Unity (asmdef) and
by dotnet (`<Compile Include>` in `games/tactics/sim/*.csproj`); there is no DLL copy.

## Battle core contract

- `Game.Create(level, rules, seed)` validates the data and starts round 1 with `first_side`.
- `LegalCommands()` lists every legal command in a fixed order; `EndTurnCommand` is always last.
- `Apply(command)` returns `StepResult` with events (`TurnStarted`, `UnitMoved`, `ShotFired`,
  `UnitKilled`, `BattleEnded`) or throws `InvalidCommandException` with no state change.
- `StateHash()`, `Clone()`, `Dump(TextWriter)`, `ToText()` serve replays, search bots,
  divergence diffs and LLM agents.

Determinism mode is `cross-device`: integers only, own `Rng` inside the state, stable hashes.

## First mechanics

| Mechanic | Rule |
|---|---|
| Turn order | Sides act in order; a side's living units get full AP at the start of its turn and lose the rest when it ends. A round ends after the last side. `max_turns` rounds, then a draw. |
| Movement | One step to any of 8 neighbours for `step_ap`; walls and living units block; no corner cutting. |
| Distance | `round(sqrt(dx² + dy²))`, computed with integers. |
| Shooting | AP cost, range, line of sight through walls only; hit chance = accuracy − falloff × cells beyond optimal range, clamped to 5..95%. |
| Burst | Each projectile rolls its own hit; a landed projectile deals its integer share of a damage roll; criticals double the share. |

## Not built yet

- Authoritative multiplayer server and network protocol (the core is designed to run inside a
  .NET server unchanged).
- Replays on disk, MCTS bots, telemetry (GameKit seams), real art, animations, UI.
- Map fairness: the first runner results put `arena_01` and `outpost_01` outside their
  `target_side0_win_percent` bands; tune the maps or the greedy bot before trusting the bands.
