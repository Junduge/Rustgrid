using System.Diagnostics;
using System.Text.Json;
using Rustgrid.Shared.Sim;
using Rustgrid.Tactics.Agents;
using Rustgrid.Tactics.Core;

// Mass bot runs over level files: levels x side agents x seeds -> JSONL rows + a summary table.
//   dotnet run --project tools/Rustgrid.Sim.Runner -c Release -- --sides greedy,greedy --episodes 1000
//   dotnet run --project tools/Rustgrid.Sim.Runner -c Release -- hash --level arena_01 --seed 42 --sides greedy,greedy

var options = Options.Parse(args);
string root = FindGameRoot();
var rules = TacticsData.ReadRules(File.ReadAllText(Path.Combine(root, "game.rules.json")));
TacticsData.ValidateRules(rules);
string rulesHash = StableHasher.ToHex(TacticsData.RulesHash(rules));

if (options.Command == "hash")
{
    // One battle, printed for cross-compiler checks against the Unity smoke run.
    var level = ReadLevel(root, options.Level ?? throw new ArgumentException("hash needs --level"));
    var summary = Episode.Play(level, rules, options.Seed, options.Sides.Select(AgentCatalog.Create).ToArray());
    Console.WriteLine($"level={level.LevelId} level_hash={StableHasher.ToHex(TacticsData.LevelHash(level))} rules_hash={rulesHash} " +
                      $"seed={options.Seed} sides={string.Join(',', options.Sides)} outcome={summary.Outcome} winner={summary.WinnerSide} " +
                      $"turns={summary.Turns} ticks={summary.Ticks} final_hash={StableHasher.ToHex(summary.FinalHash)}");
    return 0;
}

var levelIds = options.Level != null
    ? new[] { options.Level }
    : Directory.GetFiles(Path.Combine(root, "levels"), "*.json")
        .Where(p => !p.EndsWith("level.schema.json", StringComparison.Ordinal))
        .Select(p => Path.GetFileNameWithoutExtension(p)!)
        .OrderBy(id => id, StringComparer.Ordinal)
        .ToArray();

string outPath = options.Out ?? Path.Combine(root, "..", "..", "out", $"run-{options.Seed}.jsonl");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
using var output = new StreamWriter(outPath);
string runId = $"{options.Seed:x}-{string.Join('-', options.Sides)}";
Console.WriteLine($"rules_hash={rulesHash} sides={string.Join(',', options.Sides)} episodes={options.Episodes} seed={options.Seed}");
Console.WriteLine($"{"level",-14} {"level_hash",-16} {"side0 win%",10} {"side1 win%",10} {"draw%",6} {"turns p50",9} {"errors",6} {"ep/s",8}  target");

int failures = 0;
foreach (string levelId in levelIds)
{
    var level = ReadLevel(root, levelId);
    string levelHash = StableHasher.ToHex(TacticsData.LevelHash(level));
    var rows = new Row[options.Episodes];
    var clock = Stopwatch.StartNew();
    Parallel.For(0, options.Episodes, i =>
    {
        // Each episode owns its Game, agents and RNG streams; nothing is shared between threads.
        ulong seed = options.Seed + (ulong)i;
        var sides = options.Sides.Select(AgentCatalog.Create).ToArray();
        try
        {
            var s = Episode.Play(level, rules, seed, sides);
            rows[i] = new Row(runId, levelId, levelHash, rulesHash, options.Sides, seed, s.Outcome.ToString(), s.WinnerSide, s.Turns, s.Ticks, StableHasher.ToHex(s.FinalHash), null);
        }
        catch (Exception e)
        {
            rows[i] = new Row(runId, levelId, levelHash, rulesHash, options.Sides, seed, "Exception", -1, 0, 0, "", e.Message);
        }
    });
    double seconds = clock.Elapsed.TotalSeconds;
    foreach (var row in rows) output.WriteLine(JsonSerializer.Serialize(row, JsonOptions.Rows));

    int n = rows.Length;
    double Pct(Func<Row, bool> f) => 100.0 * rows.Count(f) / n;
    var turns = rows.Where(r => r.Error == null).Select(r => r.Turns).OrderBy(t => t).ToArray();
    int errors = rows.Count(r => r.Error != null);
    double side0 = Pct(r => r.Winner == 0);
    var target = level.Spec.TargetSide0WinPercent;
    bool symmetric = options.Sides.Distinct().Count() == 1;
    string verdict = target == null || !symmetric ? "-" : side0 >= target[0] && side0 <= target[1] ? $"ok [{target[0]},{target[1]}]" : $"OUT [{target[0]},{target[1]}]";
    if (errors > 0) failures++;
    Console.WriteLine($"{levelId,-14} {levelHash,-16} {side0,10:F1} {Pct(r => r.Winner == 1),10:F1} {Pct(r => r.Outcome == "Draw"),6:F1} " +
                      $"{(turns.Length > 0 ? turns[turns.Length / 2] : 0),9} {errors,6} {n / seconds,8:F0}  {verdict}");
    foreach (var bad in rows.Where(r => r.Error != null).Take(3)) Console.WriteLine($"  seed {bad.Seed}: {bad.Error}");
}
Console.WriteLine($"rows -> {Path.GetFullPath(outPath)}");
return failures == 0 ? 0 : 1;

static LevelDefinition ReadLevel(string root, string id)
{
    var level = TacticsData.ReadLevel(File.ReadAllText(Path.Combine(root, "levels", id + ".json")));
    if (level.LevelId != id) throw new InvalidDataException($"{id}.json declares level_id '{level.LevelId}'");
    return level;
}

static string FindGameRoot()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
    {
        string candidate = Path.Combine(dir.FullName, "games", "tactics");
        if (File.Exists(Path.Combine(candidate, "game.rules.json"))) return candidate;
    }
    throw new DirectoryNotFoundException("run from inside the repository (games/tactics not found)");
}

internal sealed record Row(string RunId, string LevelId, string LevelHash, string RulesHash, string[] Agents, ulong Seed,
                           string Outcome, int Winner, int Turns, int Ticks, string FinalHash, string? Error);

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Rows = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
}

internal sealed class Options
{
    public string Command { get; private set; } = "run";
    public string? Level { get; private set; }
    public string[] Sides { get; private set; } = { "greedy", "greedy" };
    public int Episodes { get; private set; } = 200;
    public ulong Seed { get; private set; } = 1;
    public string? Out { get; private set; }

    public static Options Parse(string[] args)
    {
        var o = new Options();
        int i = 0;
        if (args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal)) o.Command = args[i++];
        if (o.Command != "run" && o.Command != "hash") throw new ArgumentException($"unknown command '{o.Command}' (run, hash)");
        for (; i < args.Length; i++)
        {
            string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--level": o.Level = Value(); break;
                case "--sides": o.Sides = Value().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
                case "--episodes": o.Episodes = int.Parse(Value()); break;
                case "--seed": o.Seed = ulong.Parse(Value()); break;
                case "--out": o.Out = Value(); break;
                default: throw new ArgumentException($"unknown option {args[i]}");
            }
        }
        if (o.Episodes < 1 || o.Episodes > 1_000_000) throw new ArgumentException("--episodes must be in 1..1000000");
        foreach (string side in o.Sides) AgentCatalog.Create(side);
        return o;
    }
}
