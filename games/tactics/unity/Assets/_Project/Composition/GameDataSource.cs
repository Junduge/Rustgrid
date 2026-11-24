using System;
using System.IO;
using UnityEngine;

namespace Rustgrid.Tactics.Composition
{
    /// <summary>
    /// Where the JSON rules and levels come from. games/tactics is the single source of truth:
    /// the editor reads it in place, a player build gets a copy in StreamingAssets/tactics.
    /// </summary>
    public sealed class GameDataSource
    {
        public const string BuildFolder = "tactics";

        private readonly string _root;

        public GameDataSource(string root) => _root = root;

        /// <summary>games/tactics, two levels above the Unity project's Assets folder.</summary>
        public static string EditorRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        public static GameDataSource ForThisRuntime() =>
            new GameDataSource(Application.isEditor ? EditorRoot : Path.Combine(Application.streamingAssetsPath, BuildFolder));

        public string ReadRules() => File.ReadAllText(Path.Combine(_root, "game.rules.json"));

        public string ReadLevel(string levelId)
        {
            foreach (char c in levelId)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
                    throw new ArgumentException($"invalid level id '{levelId}'", nameof(levelId));
            return File.ReadAllText(Path.Combine(_root, "levels", levelId + ".json"));
        }
    }
}
