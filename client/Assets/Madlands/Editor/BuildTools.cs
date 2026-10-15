using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Madlands.Client.Editor
{
    /// <summary>
    /// Command-line entry points (see CLAUDE.md):
    ///   -executeMethod Madlands.Client.Editor.BuildTools.CompileCheck
    ///   -executeMethod Madlands.Client.Editor.BuildTools.BuildWindowsFromCommandLine
    ///   -executeMethod Madlands.Client.Editor.BuildTools.CaptureWorldScreenshot
    /// </summary>
    public static class BuildTools
    {
        public static string ClientBuildPath => Path.Combine(MapExporter.RepoRoot, "builds", "client", "Madlands.exe");
        public static string ScreenshotDir => Path.Combine(MapExporter.RepoRoot, "builds", "screenshots");

        /// <summary>Reaching this method means all scripts compiled.</summary>
        public static void CompileCheck()
        {
            Debug.Log("[BuildTools] Scripts compiled successfully.");
            EditorApplication.Exit(0);
        }

        [MenuItem("Madlands/Build Windows Client")]
        public static void BuildWindowsMenu() => BuildWindows();

        public static void BuildWindowsFromCommandLine() => EditorApplication.Exit(BuildWindows() ? 0 : 1);

        public static bool BuildWindows()
        {
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = ClientBuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[BuildTools] Build {summary.result}: {summary.totalSize / 1e6:0.0} MB in {summary.totalTime.TotalSeconds:0} s -> {ClientBuildPath}");
            return summary.result == BuildResult.Succeeded;
        }

        /// <summary>Renders the test world from a vantage point to builds/screenshots/editor_world.png.</summary>
        public static void CaptureWorldScreenshot()
        {
            try
            {
                EditorSceneManager.OpenScene(TestWorldBuilder.ScenePath);
                Directory.CreateDirectory(ScreenshotDir);
                // Heights are relative to the ground so the shots stay valid when the terrain changes.
                Vector3 OnGround(float x, float z, float above) => new Vector3(x, TerrainUtil.GroundHeight(new Vector3(x, 0f, z), 0f) + above, z);
                Capture(OnGround(-40f, -90f, 30f), OnGround(0f, 0f, 2f), Path.Combine(ScreenshotDir, "editor_world.png"));
                Capture(OnGround(4f, -12f, 4f), OnGround(0f, 12f, 3f), Path.Combine(ScreenshotDir, "editor_spawn.png"));
                CaptureTopDown(Path.Combine(ScreenshotDir, "editor_topdown.png"));
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Orthographic map of the whole terrain (fog off so the edges stay visible).</summary>
        private static void CaptureTopDown(string path)
        {
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            try
            {
                Capture(new Vector3(0f, 400f, 0f), Vector3.zero, path, 1024, 1024, cam =>
                {
                    cam.orthographic = true;
                    cam.orthographicSize = 256f;
                    cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                });
            }
            finally
            {
                RenderSettings.fog = fog;
            }
        }

        private static void Capture(Vector3 position, Vector3 lookAt, string path, int width = 1280, int height = 720, Action<Camera> configure = null)
        {
            var go = new GameObject("ScreenshotCamera");
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.farClipPlane = 1000f;
                cam.fieldOfView = 60f;
                go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position));
                configure?.Invoke(cam);

                var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render(); // warm-up: the first batch-mode render can miss material data
                cam.Render();

                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = previous;
                File.WriteAllBytes(path, tex.EncodeToPNG());
                cam.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(tex);
                Debug.Log("[BuildTools] Screenshot saved to " + path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
