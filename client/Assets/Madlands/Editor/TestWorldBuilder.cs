using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Madlands.Client.Editor
{
    /// <summary>
    /// Generates the Phase 1 test map (World_0001) entirely from code: rolling hills with a flat
    /// clearing at the spawn, a rim of hills at the edge, painted terrain layers, primitive trees
    /// and rocks, sun + fog, and the Game/Map objects. Deterministic: same code, same world.
    /// Real zones (Phase 5) are built by hand in the editor instead.
    ///   Batch: -executeMethod Madlands.Client.Editor.TestWorldBuilder.BuildFromCommandLine
    /// </summary>
    public static class TestWorldBuilder
    {
        public const string Root = "Assets/Madlands";
        public const string ScenePath = Root + "/Scenes/World_0001.unity";
        private const string TerrainDir = Root + "/Terrain";
        private const string MaterialDir = Root + "/Materials";
        private const int Resolution = 513;
        private const float Size = 512f;
        private const float MaxHeight = 100f;

        [MenuItem("Madlands/Rebuild Test World (World_0001)")]
        public static void BuildMenu()
        {
            if (!EditorUtility.DisplayDialog("Rebuild test world",
                    "Regenerates World_0001 from code and overwrites manual edits to it. Continue?", "Rebuild", "Cancel"))
                return;
            Build();
            MapExporter.ExportOpenScene();
        }

        public static void BuildFromCommandLine()
        {
            try
            {
                Build();
                MapExporter.ExportOpenScene();
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        public static void Build()
        {
            EnsureFolder(Root + "/Scenes");
            EnsureFolder(TerrainDir + "/Textures");
            EnsureFolder(MaterialDir);
            EnsureFolder(Root + "/Resources");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Map scenes hold only the world; camera, UI and networking live in the FrontEnd scene.
            Object.DestroyImmediate(Camera.main.gameObject);

            CreateMaterial(Root + "/Resources/EntityPlaceholder.mat", Color.white);
            var terrain = CreateTerrain();
            SetupLightingAndFog();
            CreateProps(terrain);
            CreateMapInfo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/SampleScene.unity") != null)
                AssetDatabase.DeleteAsset("Assets/Scenes/SampleScene.unity");
            SceneList.Apply();
            AssetDatabase.SaveAssets();
            Debug.Log("[TestWorldBuilder] Built " + ScenePath);
        }

        // ------------------------------------------------------------------ terrain

        private static float RawHeight(float x, float z) =>
            12f
            + Mathf.PerlinNoise(x * 0.0045f + 31.7f, z * 0.0045f + 11.3f) * 26f
            + Mathf.PerlinNoise(x * 0.017f + 5.1f, z * 0.017f + 77.9f) * 7f
            + Mathf.PerlinNoise(x * 0.06f + 13.3f, z * 0.06f + 3.7f) * 1.2f;

        public static float HeightAt(float x, float z)
        {
            float h = RawHeight(x, z);
            // Flat clearing around the spawn point.
            float d = Mathf.Sqrt(x * x + z * z);
            h = Mathf.Lerp(RawHeight(0f, 0f), h, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(20f, 55f, d)));
            // Hills rise toward the map edge and keep players inside.
            float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            h += Mathf.Max(0f, edge - 200f) * 0.9f;
            return Mathf.Min(h, MaxHeight - 1f);
        }

        private static Terrain CreateTerrain()
        {
            var data = new TerrainData
            {
                heightmapResolution = Resolution,
                alphamapResolution = 256,
                baseMapResolution = 512,
            };
            data.size = new Vector3(Size, MaxHeight, Size);

            // Make it an asset BEFORE painting: splat (alphamap) textures are only persisted as
            // sub-assets when the TerrainData already lives in the AssetDatabase.
            string dataPath = TerrainDir + "/World_0001_TerrainData.asset";
            AssetDatabase.DeleteAsset(dataPath);
            AssetDatabase.CreateAsset(data, dataPath);

            var heights = new float[Resolution, Resolution];
            float step = Size / (Resolution - 1);
            for (int z = 0; z < Resolution; z++)
                for (int x = 0; x < Resolution; x++)
                    heights[z, x] = HeightAt(-Size / 2 + x * step, -Size / 2 + z * step) / MaxHeight;
            data.SetHeights(0, 0, heights);

            data.terrainLayers = new[]
            {
                CreateLayer("Grass", new Color(0.30f, 0.47f, 0.18f), new Color(0.43f, 0.59f, 0.24f), 5f, 11),
                CreateLayer("Dirt", new Color(0.43f, 0.34f, 0.22f), new Color(0.56f, 0.46f, 0.31f), 4f, 22),
                CreateLayer("Rock", new Color(0.40f, 0.39f, 0.38f), new Color(0.58f, 0.57f, 0.55f), 7f, 33),
            };

            int ar = data.alphamapResolution;
            var alpha = new float[ar, ar, 3];
            for (int y = 0; y < ar; y++)
            {
                for (int x = 0; x < ar; x++)
                {
                    float nx = (x + 0.5f) / ar, nz = (y + 0.5f) / ar;
                    float wx = -Size / 2 + nx * Size, wz = -Size / 2 + nz * Size;
                    float rock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(24f, 34f, data.GetSteepness(nx, nz)));
                    float d = Mathf.Sqrt(wx * wx + wz * wz);
                    float plaza = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(9f, 15f, d));
                    float patches = Mathf.Clamp01((Mathf.PerlinNoise(wx * 0.03f + 9f, wz * 0.03f + 4f) - 0.62f) * 4f);
                    float dirt = Mathf.Max(plaza, patches) * (1f - rock);
                    alpha[y, x, 0] = Mathf.Max(0f, 1f - rock - dirt);
                    alpha[y, x, 1] = dirt;
                    alpha[y, x, 2] = rock;
                }
            }
            data.SetAlphamaps(0, 0, alpha);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.position = new Vector3(-Size / 2, 0f, -Size / 2);
            var terrain = go.GetComponent<Terrain>();
            terrain.materialTemplate = CreateMaterial(TerrainDir + "/TerrainLit.mat", Color.white, "Universal Render Pipeline/Terrain/Lit");
            terrain.heightmapPixelError = 4f;
            terrain.basemapDistance = 400f;
            terrain.drawInstanced = true;
            return terrain;
        }

        private static TerrainLayer CreateLayer(string name, Color a, Color b, float tileSize, int seed)
        {
            const int n = 128;
            var rng = new System.Random(seed);
            float[] coarse = RandomGrid(rng, 8), fine = RandomGrid(rng, 32);
            var tex = new Texture2D(n, n, TextureFormat.RGB24, false);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = (float)x / n, v = (float)y / n;
                    float t = 0.55f * PeriodicNoise(coarse, 8, u, v) + 0.35f * PeriodicNoise(fine, 32, u, v) + 0.1f * (float)rng.NextDouble();
                    tex.SetPixel(x, y, Color.Lerp(a, b, t));
                }
            }

            string texPath = $"{TerrainDir}/Textures/{name}.png";
            File.WriteAllBytes(Path.Combine(Application.dataPath, "..", texPath), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);

            var layer = new TerrainLayer
            {
                diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath),
                tileSize = new Vector2(tileSize, tileSize),
            };
            string layerPath = $"{TerrainDir}/{name}.terrainlayer";
            AssetDatabase.DeleteAsset(layerPath);
            AssetDatabase.CreateAsset(layer, layerPath);
            return layer;
        }

        private static float[] RandomGrid(System.Random rng, int size)
        {
            var grid = new float[size * size];
            for (int i = 0; i < grid.Length; i++) grid[i] = (float)rng.NextDouble();
            return grid;
        }

        /// <summary>Value noise that wraps at the texture edges, so terrain tiles show no seams.</summary>
        private static float PeriodicNoise(float[] grid, int size, float u, float v)
        {
            float fx = u * size, fy = v * size;
            int x0 = (int)fx % size, y0 = (int)fy % size;
            int x1 = (x0 + 1) % size, y1 = (y0 + 1) % size;
            float tx = Mathf.SmoothStep(0f, 1f, fx - Mathf.Floor(fx));
            float ty = Mathf.SmoothStep(0f, 1f, fy - Mathf.Floor(fy));
            float top = Mathf.Lerp(grid[y0 * size + x0], grid[y0 * size + x1], tx);
            float bottom = Mathf.Lerp(grid[y1 * size + x0], grid[y1 * size + x1], tx);
            return Mathf.Lerp(top, bottom, ty);
        }

        // ------------------------------------------------------------------ props

        private static void CreateProps(Terrain terrain)
        {
            var trunk = CreateMaterial(MaterialDir + "/TreeTrunk.mat", new Color(0.36f, 0.25f, 0.16f));
            var crown = CreateMaterial(MaterialDir + "/TreeCrown.mat", new Color(0.17f, 0.40f, 0.16f));
            var stone = CreateMaterial(MaterialDir + "/Stone.mat", new Color(0.52f, 0.51f, 0.49f));
            var parent = new GameObject("Props").transform;
            var rng = new System.Random(1234);
            var data = terrain.terrainData;

            bool TryPlace(float minDistance, float maxSteepness, out Vector3 position)
            {
                float x = (float)(rng.NextDouble() * 460 - 230), z = (float)(rng.NextDouble() * 460 - 230);
                position = new Vector3(x, terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.GetPosition().y, z);
                return x * x + z * z >= minDistance * minDistance
                       && data.GetSteepness((x + Size / 2) / Size, (z + Size / 2) / Size) <= maxSteepness;
            }

            for (int placed = 0, attempts = 0; placed < 260 && attempts < 5000; attempts++)
            {
                if (!TryPlace(22f, 28f, out var pos)) continue;
                var tree = new GameObject("Tree").transform;
                tree.SetParent(parent);
                tree.SetPositionAndRotation(pos, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
                tree.localScale = Vector3.one * (0.8f + (float)rng.NextDouble() * 0.7f);
                AddPrimitive(PrimitiveType.Cylinder, tree, new Vector3(0f, 2f, 0f), new Vector3(0.5f, 2f, 0.5f), trunk, keepCollider: true);
                AddPrimitive(PrimitiveType.Sphere, tree, new Vector3(0f, 5f, 0f), new Vector3(3.2f, 3.4f, 3.2f), crown, keepCollider: false);
                AddPrimitive(PrimitiveType.Sphere, tree, new Vector3(0.3f, 6.6f, -0.2f), new Vector3(2.2f, 2.2f, 2.2f), crown, keepCollider: false);
                placed++;
            }

            for (int placed = 0, attempts = 0; placed < 90 && attempts < 3000; attempts++)
            {
                if (!TryPlace(16f, 40f, out var pos)) continue;
                var scale = new Vector3(1.2f + (float)rng.NextDouble() * 2f, 0.7f + (float)rng.NextDouble(), 1.2f + (float)rng.NextDouble() * 2f);
                var rock = AddPrimitive(PrimitiveType.Sphere, parent, pos - Vector3.up * scale.y * 0.25f, scale, stone, keepCollider: true);
                rock.name = "Rock";
                rock.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                placed++;
            }

            // A landmark next to the spawn, so it is easy to tell where you are.
            float baseY = HeightAt(0f, 12f);
            var obelisk = AddPrimitive(PrimitiveType.Cube, parent, new Vector3(0f, baseY + 4f, 12f), new Vector3(1.2f, 8f, 1.2f), stone, keepCollider: true);
            obelisk.name = "SpawnObelisk";

            foreach (Transform child in parent.GetComponentsInChildren<Transform>())
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, StaticEditorFlags.BatchingStatic);
        }

        private static GameObject AddPrimitive(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 localScale, Material material, bool keepCollider)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static Material CreateMaterial(string path, Color color, string shader = "Universal Render Pipeline/Lit")
        {
            var material = new Material(Shader.Find(shader)) { color = color };
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // ------------------------------------------------------------------ scene objects

        private static void SetupLightingAndFog()
        {
            var sun = Object.FindAnyObjectByType<Light>();
            sun.name = "Sun";
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.intensity = 1.4f;
            sun.shadows = LightShadows.Soft;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.66f, 0.75f, 0.84f);
            RenderSettings.fogStartDistance = 110f;
            RenderSettings.fogEndDistance = 480f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.63f, 0.74f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.48f, 0.45f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.23f, 0.19f);
        }

        private static void CreateMapInfo()
        {
            var map = new GameObject("Map");
            var info = map.AddComponent<MapInfo>();
            info.mapId = 1;
            info.mapName = "Testlands";
            var spawn = new GameObject("SpawnPoint").transform;
            spawn.SetParent(map.transform);
            spawn.position = new Vector3(0f, HeightAt(0f, 0f), 0f);
            info.spawnPoint = spawn;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
