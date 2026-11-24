# Rustgrid — rules for agents

Rustgrid is a working codename: an original turn-based tactical post-apocalyptic online game
(grid battles, action points, bursts with per-projectile hits, turn order by side).
All art, names, maps, texts and data are original or licensed. Graphics come only from
licensed sources (CC0, Unity Asset Store Standard EULA, CC-BY with credits in CREDITS.md) or
original work; never copy assets or data from other games.

The repository is public and MIT-licensed. Asset Store and other paid assets forbid
redistribution of their source files: keep them out of git (see .gitignore) and record
only their names and licenses in CREDITS.md.

Architecture follows the `unity-game-architect` skill. Short version:

- `packages/com.rustgrid.shared.sim` — Rng, StableHasher, IntMath. Engine-free.
- `games/tactics/unity/Packages/com.rustgrid.tactics.core` — battle rules (`Game`, state,
  commands, events, level/rules DTOs and validation). Engine-free, netstandard2.1, C# 9.
- `games/tactics/unity/Packages/com.rustgrid.tactics.agents` — bots (`IAgent`, random, greedy,
  `Episode`). Engine-free.
- `games/tactics/unity/Assets/_Project/{Presentation,Composition,Authoring}` — Unity only:
  view, input, HUD; composition root; editor tools.
- `games/tactics/levels/*.json`, `games/tactics/game.rules.json` — the single source of truth
  for data, read by Unity, tests and the runner.
- `games/tactics/sim` — dotnet projects that compile the same core sources, plus tests.
- `tools/Rustgrid.Sim.Runner` — mass bot runs.

## Rules

- The core never references UnityEngine, has no mutable statics, no System.Random, DateTime,
  float/double, libm trigonometry, async or threads. `ArchitectureTests` enforce this.
- `Game.Apply(Command)` is the only mutation. The view reads `GameState` and `StepResult` events.
- Invalid commands throw `InvalidCommandException` and leave the state unchanged.
- Levels are identified by `(level_id, level_hash)`; never version them by hand.
- Never edit `.unity`, `.prefab`, `.asset` or `.meta` files as text. Use editor scripts
  (`Assets/_Project/Authoring/ProjectSetup.cs`).
- One Unity process per project folder. Mass runs go through the runner, never Play Mode.
- The multiplayer server does not exist yet. Local bot play in Unity is a debugging aid, not
  completion of a gameplay feature. The core must stay usable by an authoritative .NET server.

## Commands (in this order after C# edits)

```powershell
dotnet test Rustgrid.sln -c Release
dotnet run --project tools/Rustgrid.Sim.Runner -c Release -- --sides greedy,greedy --episodes 1000
dotnet run --project tools/Rustgrid.Sim.Runner -c Release -- hash --level arena_01 --seed 42 --sides greedy,greedy
& "C:\Program Files\Unity\Hub\Editor\6000.3.28f1\Editor\Unity.exe" -batchmode -nographics -projectPath games/tactics/unity -executeMethod Rustgrid.Tactics.Authoring.ProjectSetup.BatchSetupAndSmoke -expectedHash <final_hash from the line above> -logFile out/unity-setup.log
```

The Unity step needs the editor closed for this project. It rebuilds the battle scene and
checks that Unity's compiler produces the same battle hash as dotnet.

Report separately what automated checks verified, what was seen in the running game, and
what remains unverified.

## Environment

- Unity 6000.3.28f1 (6.3 LTS), URP, Input System only.
- .NET SDK 10; core and agents target netstandard2.1 / C# 9; tests and tools net10.0.
- git: `%LOCALAPPDATA%\GitHubDesktop\app-3.6.4\resources\app\git\cmd\git.exe` (no git on PATH).
