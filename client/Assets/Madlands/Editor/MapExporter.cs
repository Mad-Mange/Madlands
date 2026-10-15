using System;
using System.Globalization;
using System.IO;
using Madlands.Shared.Terrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Madlands.Client.Editor
{
    /// <summary>
    /// Writes content/maps/&lt;id&gt;/height.bin and map.json from the open scene. Uses
    /// Madlands.Shared's HeightMap writer, so the file format has a single implementation.
    ///   Batch: Unity -batchmode -projectPath client -executeMethod Madlands.Client.Editor.MapExporter.ExportFromCommandLine
    /// </summary>
    public static class MapExporter
    {
        public static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        [MenuItem("Madlands/Export Map (height map + map.json)")]
        public static void ExportMenu() => ExportOpenScene();

        public static void ExportFromCommandLine()
        {
            try
            {
                if (UnityEngine.Object.FindAnyObjectByType<MapInfo>() == null)
                    EditorSceneManager.OpenScene(TestWorldBuilder.ScenePath);
                ExportOpenScene();
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        public static string ExportOpenScene()
        {
            var info = UnityEngine.Object.FindAnyObjectByType<MapInfo>();
            if (info == null) throw new InvalidOperationException("The open scene has no MapInfo component.");
            var terrains = UnityEngine.Object.FindObjectsByType<Terrain>();
            if (terrains.Length == 0) throw new InvalidOperationException("The open scene has no terrains.");

            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var t in terrains)
            {
                var p = t.GetPosition();
                var size = t.terrainData.size;
                minX = Mathf.Min(minX, p.x);
                minZ = Mathf.Min(minZ, p.z);
                maxX = Mathf.Max(maxX, p.x + size.x);
                maxZ = Mathf.Max(maxZ, p.z + size.z);
            }

            float cell = Mathf.Max(0.25f, info.heightSampleSpacing);
            int width = Mathf.FloorToInt((maxX - minX) / cell + 0.001f) + 1;
            int depth = Mathf.FloorToInt((maxZ - minZ) / cell + 0.001f) + 1;
            var heightMap = HeightMap.Create(minX, minZ, cell, width, depth, (x, z) => Sample(terrains, x, z));

            var dir = Path.Combine(RepoRoot, "content", "maps", info.mapId.ToString("0000", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(dir);
            heightMap.Save(Path.Combine(dir, "height.bin"));

            var spawn = info.spawnPoint != null ? info.spawnPoint : info.transform;
            File.WriteAllText(Path.Combine(dir, "map.json"), string.Format(CultureInfo.InvariantCulture,
@"{{
  ""$schema"": ""../../schemas/map.schema.json"",
  ""_generated"": ""Written by Unity (Madlands > Export Map). Edit the scene, not this file."",
  ""id"": {0},
  ""name"": ""{1}"",
  ""heightmap"": ""height.bin"",
  ""spawn"": {{ ""x"": {2:0.00}, ""z"": {3:0.00}, ""yawDegrees"": {4:0.0} }}
}}
", info.mapId, info.mapName.Replace("\"", "'"), spawn.position.x, spawn.position.z, spawn.eulerAngles.y));

            string summary = $"Exported map {info.mapId} '{info.mapName}': {width}x{depth} samples @ {cell} m, x {minX}..{maxX}, z {minZ}..{maxZ} -> {dir}";
            Debug.Log("[MapExporter] " + summary);
            return summary;
        }

        private static float Sample(Terrain[] terrains, float x, float z)
        {
            foreach (var t in terrains)
            {
                var p = t.GetPosition();
                var size = t.terrainData.size;
                if (x < p.x || z < p.z || x > p.x + size.x || z > p.z + size.z) continue;
                return t.SampleHeight(new Vector3(x, 0f, z)) + p.y;
            }
            return 0f;
        }
    }
}
