# Rustgrid

Working codename for an original turn-based tactical game set in a post-apocalyptic world,
built with Unity 6.3 LTS (URP).

The battle rules live in an engine-free C# core that Unity and plain .NET compile from the same
sources, so bots and tests can play thousands of battles without Unity.

## Layout

| Path | What |
|---|---|
| `games/tactics/unity` | Unity project (open this folder in Unity Hub) |
| `games/tactics/unity/Packages/com.rustgrid.tactics.core` | Battle rules: state, commands, events, data validation |
| `games/tactics/unity/Packages/com.rustgrid.tactics.agents` | Bots |
| `packages/com.rustgrid.shared.sim` | Deterministic RNG, stable hashing, integer math |
| `games/tactics/levels`, `games/tactics/game.rules.json` | Maps and weapon rules (JSON) |
| `games/tactics/sim` | .NET projects and tests for the core |
| `tools/Rustgrid.Sim.Runner` | Mass bot battles with per-map statistics |

## Quick start

```powershell
dotnet test Rustgrid.sln -c Release
dotnet run --project tools/Rustgrid.Sim.Runner -c Release -- --sides greedy,greedy --episodes 1000
```

In Unity, open `Assets/_Project/Scenes/Battle.unity` and press Play. Click your blue unit,
then a neighbouring cell to step or a red unit to shoot; Space ends the turn.

See [AGENTS.md](AGENTS.md) for project rules and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
for the design. The art is placeholder geometry for now.

## License

The code is released under the [MIT License](LICENSE). Third-party assets added later keep
their own licenses, listed in [CREDITS.md](CREDITS.md).
