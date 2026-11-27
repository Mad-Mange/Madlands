using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Madlands.Client.Editor
{
    /// <summary>
    /// The playtest client: the normal Windows client plus the MADLANDS_PLAYTEST scripting define, which
    /// compiles in the PlaytestController (tools/AICritic, /critic-explore). Output: builds/playtest/Madlands.exe.
    /// Normal builds (BuildTools) never contain the controller.
    ///   -executeMethod Madlands.Client.Editor.PlaytestBuild.BuildFromCommandLine
    /// </summary>
    public static class PlaytestBuild
    {
        public const string Define = "MADLANDS_PLAYTEST";

        public static string OutputPath => Path.Combine(MapExporter.RepoRoot, "builds", "playtest", "Madlands.exe");

        [MenuItem("Madlands/Build Playtest Client")]
        public static void BuildMenu() => Build();

        public static void BuildFromCommandLine() => EditorApplication.Exit(Build() ? 0 : 1);

        public static bool Build()
        {
            // The same preparation as BuildTools.BuildWindows.
            ArtImporter.RemeasureSkeletons();
            ArtImporter.RefreshCatalog();
            SoundImporter.RefreshCatalog();
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
                extraScriptingDefines = new[] { Define },
            };
            var summary = BuildPipeline.BuildPlayer(options).summary;
            Debug.Log($"[PlaytestBuild] Build {summary.result}: {summary.totalSize / 1e6:0.0} MB in {summary.totalTime.TotalSeconds:0} s -> {OutputPath}");
            return summary.result == BuildResult.Succeeded;
        }
    }
}
