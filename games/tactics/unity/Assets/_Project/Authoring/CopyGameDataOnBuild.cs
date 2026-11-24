using System.IO;
using Rustgrid.Tactics.Composition;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Rustgrid.Tactics.Authoring
{
    /// <summary>
    /// Copies games/tactics rules and levels next to a player build, so the Assets folder never
    /// holds a second copy of the data. Only Windows standalone is implemented so far.
    /// </summary>
    public sealed class CopyGameDataOnBuild : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            var target = report.summary.platform;
            if (target != BuildTarget.StandaloneWindows64 && target != BuildTarget.StandaloneWindows)
                throw new BuildFailedException($"copying game data is implemented only for Windows standalone, not {target}");

            string exe = report.summary.outputPath;
            string destination = Path.Combine(Path.GetDirectoryName(exe)!, Path.GetFileNameWithoutExtension(exe) + "_Data",
                "StreamingAssets", GameDataSource.BuildFolder);
            string source = GameDataSource.EditorRoot;
            Directory.CreateDirectory(Path.Combine(destination, "levels"));
            File.Copy(Path.Combine(source, "game.rules.json"), Path.Combine(destination, "game.rules.json"), true);
            foreach (string level in Directory.GetFiles(Path.Combine(source, "levels"), "*.json"))
            {
                if (Path.GetFileName(level) == "level.schema.json") continue;
                File.Copy(level, Path.Combine(destination, "levels", Path.GetFileName(level)), true);
            }
        }
    }
}
