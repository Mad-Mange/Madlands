using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Madlands.Client.Editor
{
    /// <summary>
    /// Generates Hollowmere (map 1, World_0001) from code, the way the art is made from scripts
    /// (ADR 0012): a shallow mere in the middle of a 1 km valley, the village of Mirewatch on its south
    /// shore, Westbrook's farms, the Reedfen marsh, the Greywood, the Shardscar quarry and the Drowned
    /// Ruins, with the Blender props, creature spawns and NPCs. Deterministic: same code, same world.
    /// The layout and its story are in docs/zones/hollowmere.md; quests.json explore objectives use the
    /// places below.
    ///   Batch: -executeMethod Madlands.Client.Editor.HollowmereBuilder.BuildFromCommandLine
    /// </summary>
    public static class HollowmereBuilder
    {
        public const string Root = "Assets/Madlands";
        public const string ScenePath = Root + "/Scenes/World_0001.unity";
        private const string TerrainDir = Root + "/Terrain";
        private const string MaterialDir = Root + "/Materials";
        private const string PropDir = Root + "/Art/Props";
        private const int Resolution = 1025;
        private const float Size = 1024f;
        private const float MaxHeight = 160f;
        /// <summary>The surface of the mere and every pool in the marsh. Water is shallow: you wade, never swim.</summary>
        public const float WaterLevel = 21f;
        private const float Ground = 24f;

        // Places (meters; x = east, z = north). docs/zones/hollowmere.md has the map.
        public static readonly Vector2 Village = new Vector2(0f, -140f);
        private const float VillageRadius = 50f;
        public static readonly Vector2 LakeCenter = new Vector2(0f, 60f);
        private static readonly Vector2 LakeRadii = new Vector2(165f, 118f);
        public static readonly Vector2 Farm = new Vector2(-260f, -150f);
        public static readonly Vector2 OldMill = new Vector2(-390f, -40f);
        public static readonly Vector2 Reedfen = new Vector2(275f, -40f);
        public static readonly Vector2 OracleStone = new Vector2(300f, -5f);
        public static readonly Vector2 Greywood = new Vector2(-285f, 235f);
        public static readonly Vector2 RennickTower = new Vector2(-175f, 40f);
        public static readonly Vector2 Shardscar = new Vector2(265f, 265f);
        public static readonly Vector2 Quarry = new Vector2(300f, 300f);
        public static readonly Vector2 BanditCamp = new Vector2(225f, 215f);
        public static readonly Vector2 BrannocCamp = new Vector2(160f, 130f);
        public static readonly Vector2 Ruins = new Vector2(0f, 320f);
        public static readonly Vector2 BellTower = new Vector2(15f, 352f);
        public static readonly Vector2 Shrine = new Vector2(-110f, 250f);
        public static readonly Vector2 Lagoon = new Vector2(70f, 395f);

        /// <summary>Roads as polylines; painted as dirt.</summary>
        private static readonly Vector2[][] Roads =
        {
            // North gate to the dock, west gate to the farms and the old mill, east gate to the marsh.
            new[] { new Vector2(0f, -90f), new Vector2(0f, -66f) },
            new[] { new Vector2(-50f, -140f), new Vector2(-120f, -150f), new Vector2(-200f, -150f), new Vector2(-240f, -145f), new Vector2(-320f, -100f), new Vector2(-390f, -45f) },
            new[] { new Vector2(50f, -140f), new Vector2(120f, -130f), new Vector2(190f, -90f), new Vector2(240f, -60f) },
            // Around the west shore to Rennick's tower, the Greywood and the shrine.
            new[] { new Vector2(-35f, -105f), new Vector2(-120f, -60f), new Vector2(-175f, 25f), new Vector2(-200f, 120f), new Vector2(-170f, 200f), new Vector2(-110f, 245f) },
            // The shrine to the ruins.
            new[] { new Vector2(-110f, 250f), new Vector2(-50f, 290f), new Vector2(0f, 310f) },
            // Around the east shore to Brannoc, the bandit camp and the quarry.
            new[] { new Vector2(35f, -105f), new Vector2(150f, -20f), new Vector2(190f, 60f), new Vector2(160f, 125f), new Vector2(205f, 200f), new Vector2(285f, 285f) },
        };

        [MenuItem("Madlands/Rebuild Hollowmere (World_0001)")]
        public static void BuildMenu()
        {
            if (!EditorUtility.DisplayDialog("Rebuild Hollowmere",
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
            CreateWater();
            SetupLightingAndFog();
            var rng = new System.Random(2026);
            CreateVillage(terrain);
            CreateFarms(terrain, rng);
            CreateWilds(terrain, rng);
            CreateCamps(terrain, rng);
            CreateRuins(terrain, rng);
            CreateSpawns(terrain, rng);
            CreateMapInfo(terrain);

            EditorSceneManager.SaveScene(scene, ScenePath);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/SampleScene.unity") != null)
                AssetDatabase.DeleteAsset("Assets/Scenes/SampleScene.unity");
            SceneList.Apply();
            AssetDatabase.SaveAssets();
            Debug.Log("[HollowmereBuilder] Built " + ScenePath);
        }

        // ------------------------------------------------------------------ terrain shape

        private static float Noise(float x, float z, float frequency, float amplitude, float seed = 0f) =>
            (Mathf.PerlinNoise(x * frequency + 31.7f + seed, z * frequency + 11.3f + seed * 1.7f) - 0.5f) * 2f * amplitude;

        private static float Smooth01(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

        /// <summary>1 at the center, fading to 0 at <paramref name="radius"/>.</summary>
        private static float Influence(float x, float z, Vector2 center, float radius) =>
            1f - Smooth01(Vector2.Distance(new Vector2(x, z), center) / radius);

        /// <summary>0 at the center of the mere, 1 on its shore (a little ragged).</summary>
        public static float LakeDistance(float x, float z)
        {
            float dx = (x - LakeCenter.x) / LakeRadii.x, dz = (z - LakeCenter.y) / LakeRadii.y;
            return Mathf.Sqrt(dx * dx + dz * dz) * (1f + Noise(x, z, 0.02f, 0.07f, 5f));
        }

        public static float HeightAt(float x, float z)
        {
            float h = Ground + Noise(x, z, 0.004f, 3.5f) + Noise(x, z, 0.015f, 1.2f, 3f) + Noise(x, z, 0.06f, 0.25f, 7f);
            // The Greywood rolls; Shardscar rises into rocky hills around a quarry pit.
            h += Influence(x, z, Greywood, 240f) * (6f + Noise(x, z, 0.013f, 7f, 9f));
            h += Influence(x, z, Shardscar, 210f) * (16f + Noise(x, z, 0.02f, 5f, 13f));
            h -= (1f - Smooth01(Vector2.Distance(new Vector2(x, z), Quarry) / 70f)) * 13f;
            // Mountains close the valley.
            float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) + Noise(x, z, 0.008f, 28f, 23f);
            float rim = Mathf.Max(0f, edge - 380f) / 130f;
            h += rim * rim * 95f + Smooth01((edge - 390f) / 70f) * (Noise(x, z, 0.02f, 14f, 17f) + Noise(x, z, 0.07f, 3f, 29f));

            // Flat ground for the village and the fields.
            h = Mathf.Lerp(h, Ground + 1.5f, Mathf.Clamp01(Influence(x, z, Village, VillageRadius + 45f) * 1.4f));
            h = Mathf.Lerp(h, Ground + 0.5f + Noise(x, z, 0.01f, 0.6f), Mathf.Clamp01(Influence(x, z, Farm, 140f) * 1.5f));

            // Reedfen: just above and below the water, so pools appear by themselves.
            float marsh = Mathf.Clamp01(Influence(x, z, Reedfen, 160f) * 1.6f);
            h = Mathf.Lerp(h, WaterLevel + 0.25f + Noise(x, z, 0.035f, 0.75f, 21f), marsh);
            // The lagoon by the ruins, where something large sleeps.
            float lagoon = Vector2.Distance(new Vector2(x, z), Lagoon) / 55f;
            if (lagoon < 1.4f) h = Mathf.Lerp(WaterLevel - 0.9f, h, Smooth01((lagoon - 0.7f) / 0.7f));

            // The mere: a wide shallow bowl with a beach.
            float d = LakeDistance(x, z);
            if (d < 1f) h = WaterLevel - 0.2f - 0.7f * Smooth01((1f - d) / 0.45f);
            else if (d < 1.3f) h = Mathf.Lerp(WaterLevel + 0.15f, h, Smooth01((d - 1f) / 0.3f));
            return Mathf.Clamp(h, 1f, MaxHeight - 1f);
        }

        private static float DistanceToRoad(Vector2 p)
        {
            float best = float.MaxValue;
            foreach (var road in Roads)
            {
                for (int i = 0; i + 1 < road.Length; i++)
                {
                    var a = road[i];
                    var ab = road[i + 1] - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                    best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
                }
            }
            return best;
        }

        // ------------------------------------------------------------------ terrain asset

        private static Terrain CreateTerrain()
        {
            var data = new TerrainData
            {
                heightmapResolution = Resolution,
                alphamapResolution = 512,
                baseMapResolution = 512,
            };
            data.size = new Vector3(Size, MaxHeight, Size);
            // An asset BEFORE painting: splat textures are only kept as sub-assets of a saved TerrainData.
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
                CreateLayer("Grass", new Color(0.31f, 0.47f, 0.19f), new Color(0.44f, 0.58f, 0.25f), 5f, 11),
                CreateLayer("Dirt", new Color(0.45f, 0.36f, 0.24f), new Color(0.57f, 0.47f, 0.32f), 4f, 22),
                CreateLayer("Rock", new Color(0.41f, 0.40f, 0.39f), new Color(0.58f, 0.57f, 0.55f), 7f, 33),
                CreateLayer("Mud", new Color(0.24f, 0.25f, 0.15f), new Color(0.35f, 0.33f, 0.21f), 4f, 44),
                CreateLayer("Forest", new Color(0.17f, 0.29f, 0.13f), new Color(0.27f, 0.37f, 0.17f), 5f, 55),
            };

            int ar = data.alphamapResolution;
            var alpha = new float[ar, ar, 5];
            for (int y = 0; y < ar; y++)
            {
                for (int x = 0; x < ar; x++)
                {
                    float nx = (x + 0.5f) / ar, nz = (y + 0.5f) / ar;
                    float wx = -Size / 2 + nx * Size, wz = -Size / 2 + nz * Size;
                    float height = HeightAt(wx, wz);
                    float rock = Smooth01((data.GetSteepness(nx, nz) - 24f) / 12f);
                    rock = Mathf.Max(rock, Mathf.Clamp01(Influence(wx, wz, Quarry, 80f) * 1.5f));
                    float mud = Smooth01((WaterLevel + 1.2f - height) / 1.2f);
                    float road = 1f - Smooth01((DistanceToRoad(new Vector2(wx, wz)) - 2.2f) / 2f);
                    float village = Influence(wx, wz, Village, VillageRadius - 5f) * 2f;
                    float fields = FieldMask(wx, wz);
                    float dirt = Mathf.Clamp01(Mathf.Max(Mathf.Max(road, village), fields));
                    float forest = Mathf.Clamp01(Influence(wx, wz, Greywood, 230f) * 2f) * Smooth01((Noise(wx, wz, 0.03f, 1f, 3f) + 0.6f));
                    float grass = 1f;
                    // Each layer takes its share of what is left.
                    float r = rock;
                    float m = mud * (1f - r);
                    float d = dirt * (1f - r - m);
                    float f = forest * (1f - r - m - d);
                    float g = Mathf.Max(0f, grass - r - m - d - f);
                    alpha[y, x, 0] = g;
                    alpha[y, x, 1] = d;
                    alpha[y, x, 2] = r;
                    alpha[y, x, 3] = m;
                    alpha[y, x, 4] = f;
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
            terrain.basemapDistance = 500f;
            terrain.drawInstanced = true;
            return terrain;
        }

        /// <summary>The farm fields: plowed rectangles west of the village.</summary>
        private static readonly (Vector2 center, Vector2 size, float yaw)[] Fields =
        {
            (new Vector2(-230f, -205f), new Vector2(44f, 26f), 8f),
            (new Vector2(-285f, -215f), new Vector2(38f, 28f), -6f),
            (new Vector2(-215f, -95f), new Vector2(40f, 24f), 15f),
            (new Vector2(-300f, -60f), new Vector2(36f, 30f), 25f),
        };

        private static float FieldMask(float x, float z)
        {
            float best = 0f;
            foreach (var (center, size, yaw) in Fields)
            {
                var local = Quaternion.Euler(0f, 0f, yaw) * new Vector3(x - center.x, z - center.y, 0f);
                float fx = Mathf.Abs(local.x) - size.x / 2, fz = Mathf.Abs(local.y) - size.y / 2;
                best = Mathf.Max(best, 1f - Smooth01(Mathf.Max(fx, fz) / 1.5f));
            }
            return best;
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

        /// <summary>One flat plane over the whole valley at <see cref="WaterLevel"/>; the terrain hides it on land.</summary>
        private static void CreateWater()
        {
            var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "Water";
            Object.DestroyImmediate(water.GetComponent<Collider>());
            water.transform.position = new Vector3(0f, WaterLevel, 0f);
            water.transform.localScale = new Vector3(Size / 10f, 1f, Size / 10f);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetFloat("_Surface", 1f); // transparent
            material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetColor("_BaseColor", new Color(0.2f, 0.36f, 0.4f, 0.72f));
            material.SetFloat("_Smoothness", 0.85f);
            string path = MaterialDir + "/Water.mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(material, path);
            var renderer = water.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ props

        private enum Collide { None, Mesh, Trunk }

        private static Terrain _terrain;

        private static float GroundAt(float x, float z) => _terrain.SampleHeight(new Vector3(x, 0f, z)) + _terrain.GetPosition().y;

        /// <summary>
        /// Places a Blender prop (Art/Props) on the ground. Yaw 0 faces north (+Z). Mesh colliders block
        /// players and line of sight and become navmesh obstacles; trunks get a capsule.
        /// </summary>
        private static GameObject Place(string prop, Transform parent, float x, float z, float yaw, float scale = 1f,
            Collide collide = Collide.Mesh, float sink = 0.1f, float trunkRadius = 0.4f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PropDir}/{prop}.fbx");
            if (prefab == null) throw new InvalidOperationException($"Missing prop {prop}: build it with art/blender/build.py");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(new Vector3(x, GroundAt(x, z) - sink * scale, z), Quaternion.Euler(0f, yaw, 0f) * prefab.transform.localRotation);
            go.transform.localScale = prefab.transform.localScale * scale;
            switch (collide)
            {
                case Collide.Mesh:
                    foreach (var filter in go.GetComponentsInChildren<MeshFilter>())
                        filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                    break;
                case Collide.Trunk:
                    var capsule = go.AddComponent<CapsuleCollider>();
                    capsule.radius = trunkRadius;
                    capsule.height = 5f;
                    capsule.center = new Vector3(0f, 2.5f, 0f);
                    break;
            }
            foreach (var t in go.GetComponentsInChildren<Transform>())
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
            return go;
        }

        private static Transform Group(string name) => new GameObject(name).transform;

        private static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        private static Vector2 Around(System.Random rng, Vector2 center, float radius)
        {
            float a = Range(rng, 0f, Mathf.PI * 2f), r = Mathf.Sqrt((float)rng.NextDouble()) * radius;
            return center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        /// <summary>Places that keep their ground free of scattered trees and rocks.</summary>
        private static readonly (Vector2 center, float radius)[] Clearings =
        {
            (Village, VillageRadius + 12f), (Farm, 75f), (RennickTower, 14f), (BrannocCamp, 16f), (BanditCamp, 26f),
            (Quarry, 45f), (Shrine, 18f), (Ruins, 60f), (OldMill, 14f), (OracleStone, 14f), (Lagoon, 50f),
        };

        private static bool FreeForScatter(Vector2 p, float roadMargin = 5f)
        {
            if (Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) > 470f) return false;
            if (LakeDistance(p.x, p.y) < 1.05f) return false;
            if (DistanceToRoad(p) < roadMargin) return false;
            if (FieldMask(p.x, p.y) > 0.01f) return false;
            foreach (var (center, radius) in Clearings)
                if (Vector2.Distance(p, center) < radius) return false;
            return true;
        }

        private static bool Dry(Vector2 p) => HeightAt(p.x, p.y) > WaterLevel + 0.3f;

        // ------------------------------------------------------------------ Mirewatch

        private static void CreateVillage(Terrain terrain)
        {
            _terrain = terrain;
            var parent = Group("Mirewatch");
            float vx = Village.x, vz = Village.y;

            // A palisade ring with gates to the north (the dock), west (the farms) and east (the marsh).
            const int segments = 76;
            float circumference = 2f * Mathf.PI * VillageRadius;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * 360f / segments;
                bool gate = Mathf.Abs(Mathf.DeltaAngle(angle, 0f)) < 2.5f || Mathf.Abs(Mathf.DeltaAngle(angle, 270f)) < 2.5f || Mathf.Abs(Mathf.DeltaAngle(angle, 90f)) < 2.5f;
                if (gate) continue;
                float a = angle * Mathf.Deg2Rad;
                Place("Palisade", parent, vx + Mathf.Sin(a) * VillageRadius, vz + Mathf.Cos(a) * VillageRadius, angle, circumference / segments / 4.2f, sink: 0.3f);
            }
            // A gate fills each one-segment gap; lanterns flank it outside.
            foreach (float angle in new[] { 0f, 90f, 270f })
            {
                float a = angle * Mathf.Deg2Rad;
                Place("Gate", parent, vx + Mathf.Sin(a) * VillageRadius, vz + Mathf.Cos(a) * VillageRadius, angle, 0.85f, sink: 0.3f);
                foreach (float side in new[] { -7f, 7f })
                {
                    float b = (angle + side) * Mathf.Deg2Rad;
                    Place("LanternPost", parent, vx + Mathf.Sin(b) * (VillageRadius + 3f), vz + Mathf.Cos(b) * (VillageRadius + 3f), angle + 180f, 1f, Collide.Trunk, trunkRadius: 0.15f);
                }
            }
            Place("Watchtower", parent, vx - 30f, vz + 30f, 45f);
            Place("Watchtower", parent, vx + 30f, vz + 30f, -45f);

            // The square: a well, market stalls and lanterns; the Muddy Boot inn on its north side.
            Place("Well", parent, vx, vz, 0f);
            Place("Longhouse", parent, vx, vz + 26f, 180f, sink: 0.3f);
            Place("MarketStall", parent, vx - 11f, vz + 9f, 160f);
            Place("MarketStall", parent, vx + 11f, vz + 9f, 200f);
            Place("MarketStall", parent, vx - 14f, vz - 12f, 60f);
            foreach (var (x, z) in new[] { (-7f, -7f), (7f, -7f), (-7f, 14f), (7f, 14f) })
                Place("LanternPost", parent, vx + x, vz + z, x < 0 ? 90f : 270f, 1f, Collide.Trunk, trunkRadius: 0.15f);

            // Homes around the square, facing it.
            Place("Cottage", parent, vx - 30f, vz - 6f, 90f, sink: 0.3f);
            Place("Cottage", parent, vx - 28f, vz + 14f, 110f, sink: 0.3f);
            Place("Cottage", parent, vx + 30f, vz - 6f, 270f, sink: 0.3f);
            Place("Cottage", parent, vx + 28f, vz + 14f, 250f, sink: 0.3f);
            Place("Cottage", parent, vx - 14f, vz - 34f, 10f, sink: 0.3f);
            Place("Cottage", parent, vx + 14f, vz - 34f, -10f, sink: 0.3f);
            Place("Cottage", parent, vx - 41f, vz + 2f, 90f, sink: 0.3f);
            Place("Cottage", parent, vx + 41f, vz + 2f, 270f, sink: 0.3f);
            Place("Cottage", parent, vx, vz - 41f, 0f, sink: 0.3f);
            // Gardens and clutter between the houses.
            var clutter = new System.Random(5);
            foreach (var (x, z) in new[] { (-34f, -16f), (34f, 22f), (-6f, -24f), (24f, 32f), (-20f, 30f), (40f, -10f), (-44f, -14f), (8f, -46f) })
            {
                Place(clutter.NextDouble() < 0.5 ? "Bush1" : "Bush2", parent, vx + x, vz + z, (float)clutter.NextDouble() * 360f, 0.8f, Collide.None);
                Place(clutter.NextDouble() < 0.5 ? "Barrel" : "Sacks", parent, vx + x + 2.5f, vz + z + 1.5f, (float)clutter.NextDouble() * 360f, 1f,
                    clutter.NextDouble() < 0.5 ? Collide.Mesh : Collide.None);
            }
            foreach (var (x, z, yaw) in new[] { (-36f, 8f, 90f), (-36f, 11f, 90f), (36f, -12f, 90f), (36f, -15f, 90f) })
                Place("Fence", parent, vx + x, vz + z, yaw);

            // Old Tamsin's garden west of the square: her cottage, the cauldron, mushrooms and herbs.
            Place("Cauldron", parent, vx - 22f, vz - 18f, 0f);
            Place("Mushrooms", parent, vx - 25f, vz - 21f, 40f, 1f, Collide.None);
            Place("Bush1", parent, vx - 26f, vz - 16f, 0f, 0.6f, Collide.None);
            Place("Sacks", parent, vx - 19f, vz - 21f, 20f, 1f, Collide.None);

            // The captain's yard east: a tent, the banner and practice dummies (spawned as creatures).
            Place("Tent", parent, vx + 26f, vz - 26f, 300f);
            Place("BannerPole", parent, vx + 18f, vz - 20f, 250f, 1f, Collide.Trunk, trunkRadius: 0.12f);
            Place("Crate", parent, vx + 31f, vz - 20f, 15f);
            Place("Barrel", parent, vx + 31f, vz - 18.5f, 0f);

            // Holt's supplies and Gerta's smithy by the square.
            Place("Tent", parent, vx + 18f, vz - 2f, 270f);
            foreach (var (x, z, yaw) in new[] { (14f, -6f, 10f), (15.2f, -7.1f, 35f), (14.6f, 4f, 70f) })
                Place("Crate", parent, vx + x, vz + z, yaw);
            Place("Barrel", parent, vx + 16f, vz + 5.5f, 0f);
            Place("Cauldron", parent, vx - 17f, vz - 7f, 0f, 1.2f);
            Place("Barrel", parent, vx - 18.5f, vz - 4f, 0f);

            // The dock on the mere, north of the gate.
            Place("Dock", parent, 0f, -61f, 0f, 1f, Collide.None, sink: 0f);
            Place("Barrel", parent, 2.2f, -64f, 0f);
            Place("Signpost", parent, 4f, -92f, 0f, 1f, Collide.Trunk, trunkRadius: 0.1f);
            Place("Signpost", parent, vx - VillageRadius - 6f, vz - 4f, 270f, 1f, Collide.Trunk, trunkRadius: 0.1f);
            Place("Signpost", parent, vx + VillageRadius + 6f, vz + 4f, 90f, 1f, Collide.Trunk, trunkRadius: 0.1f);
        }

        // ------------------------------------------------------------------ Westbrook

        private static void CreateFarms(Terrain terrain, System.Random rng)
        {
            var parent = Group("Westbrook");
            Place("Cottage", parent, Farm.x + 5f, Farm.y + 10f, 120f, 1.1f, sink: 0.3f);
            Place("Longhouse", parent, Farm.x - 18f, Farm.y - 14f, 90f, sink: 0.3f);
            Place("Well", parent, Farm.x - 4f, Farm.y - 4f, 30f);
            Place("Sacks", parent, Farm.x - 8f, Farm.y - 22f, 0f, 1f, Collide.None);
            Place("Barrel", parent, Farm.x + 9f, Farm.y + 4f, 0f);
            // Fences along the long sides of each field.
            foreach (var (center, size, yaw) in Fields)
            {
                var along = Quaternion.Euler(0f, -yaw, 0f) * Vector3.right;
                var across = Quaternion.Euler(0f, -yaw, 0f) * Vector3.forward;
                int count = Mathf.FloorToInt(size.x / 3.1f);
                foreach (float side in new[] { -1f, 1f })
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (i == count / 2) continue; // a gap to walk through
                        var p = new Vector3(center.x, 0f, center.y) + along * (-size.x / 2 + 1.55f + i * 3.1f) + across * side * (size.y / 2 + 1f);
                        Place("Fence", parent, p.x, p.z, -yaw, 1f, Collide.Mesh, sink: 0.05f);
                    }
                }
            }
            // The old mill: broken walls and a fallen beam on the stream bank (an explore objective).
            var mill = Group("OldMill");
            Place("RuinWall", mill, OldMill.x, OldMill.y, 30f);
            Place("RuinWall", mill, OldMill.x + 4f, OldMill.y - 5f, 120f);
            Place("RuinPillar", mill, OldMill.x - 4f, OldMill.y + 3f, 0f);
            Place("Log", mill, OldMill.x + 2f, OldMill.y + 4f, 70f);
            Place("TreeDead", mill, OldMill.x - 7f, OldMill.y - 6f, 0f, 1f, Collide.Trunk, trunkRadius: 0.3f);
        }

        // ------------------------------------------------------------------ nature

        private static void CreateWilds(Terrain terrain, System.Random rng)
        {
            var trees = Group("Trees");
            var plants = Group("Plants");
            var stones = Group("Rocks");

            // The Greywood: dense pines and oaks, mushrooms, logs and stumps.
            for (int placed = 0, tries = 0; placed < 1000 && tries < 20000; tries++)
            {
                var p = Around(rng, Greywood, 235f);
                // Thinner toward the edge of the wood.
                if (rng.NextDouble() > 0.35 + Influence(p.x, p.y, Greywood, 235f) * 1.3) continue;
                if (!FreeForScatter(p, 4f) || !Dry(p)) continue;
                bool pine = rng.NextDouble() < 0.6;
                string model = pine ? (rng.NextDouble() < 0.5 ? "TreePine1" : "TreePine2") : (rng.NextDouble() < 0.5 ? "TreeOak1" : "TreeOak2");
                Place(model, trees, p.x, p.y, Range(rng, 0f, 360f), Range(rng, 0.8f, 1.3f), Collide.Trunk, sink: 0.2f, trunkRadius: pine ? 0.3f : 0.38f);
                placed++;
                if (rng.NextDouble() < 0.12) Place("Mushrooms", plants, p.x + 2f, p.y + 1f, Range(rng, 0f, 360f), 1f, Collide.None, sink: 0f);
                if (rng.NextDouble() < 0.05) Place("Log", plants, p.x - 3f, p.y + 2f, Range(rng, 0f, 360f), 1f, Collide.Mesh, sink: 0.15f);
                if (rng.NextDouble() < 0.05) Place("Stump", plants, p.x + 3f, p.y - 2f, Range(rng, 0f, 360f), 1f, Collide.Mesh);
            }

            // Scattered oaks, bushes and rocks over the rest of the valley.
            for (int placed = 0, tries = 0; placed < 170 && tries < 8000; tries++)
            {
                var p = new Vector2(Range(rng, -460f, 460f), Range(rng, -460f, 460f));
                if (Influence(p.x, p.y, Greywood, 240f) > 0f || Influence(p.x, p.y, Reedfen, 150f) > 0.3f) continue;
                if (!FreeForScatter(p) || !Dry(p)) continue;
                string model = rng.NextDouble() < 0.7 ? (rng.NextDouble() < 0.5 ? "TreeOak1" : "TreeOak2") : "TreePine1";
                Place(model, trees, p.x, p.y, Range(rng, 0f, 360f), Range(rng, 0.8f, 1.3f), Collide.Trunk, sink: 0.2f, trunkRadius: 0.35f);
                placed++;
            }
            for (int placed = 0, tries = 0; placed < 160 && tries < 6000; tries++)
            {
                var p = new Vector2(Range(rng, -460f, 460f), Range(rng, -460f, 460f));
                if (!FreeForScatter(p, 3f) || !Dry(p)) continue;
                Place(rng.NextDouble() < 0.5 ? "Bush1" : "Bush2", plants, p.x, p.y, Range(rng, 0f, 360f), Range(rng, 0.7f, 1.3f), Collide.None, sink: 0.1f);
                placed++;
            }
            for (int placed = 0, tries = 0; placed < 140 && tries < 6000; tries++)
            {
                var p = new Vector2(Range(rng, -470f, 470f), Range(rng, -470f, 470f));
                // More stone toward Shardscar and the mountains.
                float rocky = Mathf.Max(Influence(p.x, p.y, Shardscar, 220f), Smooth01((Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) - 380f) / 60f));
                if (rng.NextDouble() > 0.35f + rocky || !FreeForScatter(p, 3f)) continue;
                string model = rng.NextDouble() < 0.12 ? "Boulder" : rng.NextDouble() < 0.5 ? "Rock1" : "Rock2";
                Place(model, stones, p.x, p.y, Range(rng, 0f, 360f), Range(rng, 0.7f, 1.6f), Collide.Mesh, sink: 0.4f);
                placed++;
            }

            // Reeds along the shore of the mere and all over Reedfen; dead trees in the marsh.
            for (int placed = 0, tries = 0; placed < 380 && tries < 20000; tries++)
            {
                Vector2 p;
                if (placed % 2 == 0)
                {
                    float a = Range(rng, 0f, Mathf.PI * 2f), d = Range(rng, 0.9f, 1.08f);
                    p = LakeCenter + new Vector2(Mathf.Cos(a) * LakeRadii.x * d, Mathf.Sin(a) * LakeRadii.y * d);
                }
                else p = Around(rng, Reedfen, 150f);
                float h = HeightAt(p.x, p.y);
                if (h < WaterLevel - 0.6f || h > WaterLevel + 0.6f || DistanceToRoad(p) < 4f) continue;
                if (Vector2.Distance(p, new Vector2(0f, -64f)) < 10f || Vector2.Distance(p, OracleStone) < 12f) continue;
                Place("Reeds", plants, p.x, p.y, Range(rng, 0f, 360f), Range(rng, 0.8f, 1.4f), Collide.None, sink: 0f);
                placed++;
            }
            for (int placed = 0, tries = 0; placed < 30 && tries < 2000; tries++)
            {
                var p = Around(rng, Reedfen, 150f);
                if (!FreeForScatter(p)) continue;
                Place("TreeDead", trees, p.x, p.y, Range(rng, 0f, 360f), Range(rng, 0.8f, 1.3f), Collide.Trunk, sink: 0.3f, trunkRadius: 0.3f);
                placed++;
            }
        }

        // ------------------------------------------------------------------ camps

        private static void CreateCamps(Terrain terrain, System.Random rng)
        {
            // Scout Rennick's watchtower at the edge of the Greywood.
            var scout = Group("RennicksWatch");
            Place("Watchtower", scout, RennickTower.x, RennickTower.y, 120f);
            Place("Tent", scout, RennickTower.x + 6f, RennickTower.y - 5f, 200f);
            Place("Campfire", scout, RennickTower.x + 3f, RennickTower.y - 9f, 0f, 1f, Collide.None, sink: 0f);
            Place("Log", scout, RennickTower.x + 5f, RennickTower.y - 11.5f, 20f, 0.5f, Collide.None);

            // Brannoc Ironvein's prospecting camp on the quarry road.
            var korrin = Group("IronveinCamp");
            Place("Tent", korrin, BrannocCamp.x, BrannocCamp.y + 4f, 160f);
            Place("Campfire", korrin, BrannocCamp.x - 3f, BrannocCamp.y - 2f, 0f, 1f, Collide.None, sink: 0f);
            Place("Crate", korrin, BrannocCamp.x + 4f, BrannocCamp.y + 1f, 20f);
            Place("Barrel", korrin, BrannocCamp.x + 4.5f, BrannocCamp.y - 1f, 0f);
            Place("Signpost", korrin, BrannocCamp.x - 6f, BrannocCamp.y - 6f, 40f, 1f, Collide.Trunk, trunkRadius: 0.1f);

            // The bandit camp: a palisade ring open toward the road, tents, loot and a fire.
            var bandits = Group("BanditCamp");
            const float campRadius = 17f;
            for (int i = 0; i < 22; i++)
            {
                float angle = i * 360f / 22f;
                if (Mathf.Abs(Mathf.DeltaAngle(angle, 225f)) < 22f) continue;
                float a = angle * Mathf.Deg2Rad;
                Place("Palisade", bandits, BanditCamp.x + Mathf.Sin(a) * campRadius, BanditCamp.y + Mathf.Cos(a) * campRadius, angle, 1.2f, sink: 0.3f);
            }
            Place("Tent", bandits, BanditCamp.x + 7f, BanditCamp.y + 6f, 200f, 1.2f);
            Place("Tent", bandits, BanditCamp.x - 6f, BanditCamp.y + 8f, 160f, 1.2f);
            Place("Tent", bandits, BanditCamp.x + 9f, BanditCamp.y - 4f, 250f, 1.1f);
            Place("Campfire", bandits, BanditCamp.x, BanditCamp.y, 0f, 1.2f, Collide.None, sink: 0f);
            for (int i = 0; i < 8; i++)
            {
                var p = Around(rng, BanditCamp, 12f);
                if (Vector2.Distance(p, BanditCamp) < 4f) continue;
                Place(rng.NextDouble() < 0.6 ? "Crate" : "Barrel", bandits, p.x, p.y, Range(rng, 0f, 90f));
            }
            Place("CrystalCluster", bandits, BanditCamp.x - 4f, BanditCamp.y - 3f, 30f, 0.6f, Collide.None);

            // The quarry pit: crystals in the rock, the lieutenant's crates.
            var quarry = Group("Quarry");
            for (int i = 0; i < 16; i++)
            {
                var p = Around(rng, Quarry, 55f);
                Place("CrystalCluster", quarry, p.x, p.y, Range(rng, 0f, 360f), Range(rng, 0.8f, 1.8f), Collide.Mesh, sink: 0.3f);
            }
            for (int i = 0; i < 10; i++)
            {
                var p = Around(rng, Quarry, 60f);
                Place(rng.NextDouble() < 0.5 ? "Rock2" : "Boulder", quarry, p.x, p.y, Range(rng, 0f, 360f), Range(rng, 0.6f, 1.2f), Collide.Mesh, sink: 0.5f);
            }
            Place("Tent", quarry, Quarry.x + 6f, Quarry.y + 5f, 210f);
            Place("Crate", quarry, Quarry.x + 3f, Quarry.y + 9f, 10f);
            Place("Crate", quarry, Quarry.x + 4.3f, Quarry.y + 9.4f, 40f);

            // Mireling camps in Reedfen: reed huts around a fire, and the singing stone.
            var mirelings = Group("Reedfen");
            foreach (var camp in new[] { new Vector2(235f, -95f), new Vector2(325f, -70f), new Vector2(255f, 40f) })
            {
                Place("Tent", mirelings, camp.x + 4f, camp.y + 3f, Range(rng, 0f, 360f), 0.9f);
                Place("Tent", mirelings, camp.x - 4f, camp.y + 2f, Range(rng, 0f, 360f), 0.8f);
                Place("Campfire", mirelings, camp.x, camp.y - 2f, 0f, 0.8f, Collide.None, sink: 0f);
                Place("Reeds", mirelings, camp.x + 6f, camp.y - 4f, 0f, 1.3f, Collide.None, sink: 0f);
            }
            Place("CrystalCluster", mirelings, OracleStone.x, OracleStone.y, 0f, 2.6f, Collide.Mesh, sink: 0.5f);
        }

        // ------------------------------------------------------------------ the north shore

        private static void CreateRuins(Terrain terrain, System.Random rng)
        {
            var parent = Group("DrownedRuins");
            for (int i = 0; i < 44; i++)
            {
                var p = Around(rng, Ruins, 60f);
                if (Vector2.Distance(p, BellTower) < 8f) continue;
                if (rng.NextDouble() < 0.55) Place("RuinWall", parent, p.x, p.y, Range(rng, 0f, 360f), Range(rng, 0.9f, 1.4f), Collide.Mesh, sink: 0.4f);
                else Place("RuinPillar", parent, p.x, p.y, Range(rng, 0f, 360f), Range(rng, 0.9f, 1.3f), Collide.Mesh, sink: 0.3f);
            }
            // The sunken bell tower leans over the water.
            var tower = Place("Watchtower", parent, BellTower.x, BellTower.y, 200f, 1.15f, Collide.Mesh, sink: 1.2f);
            tower.transform.rotation = Quaternion.Euler(9f, 0f, 6f) * tower.transform.rotation;
            for (int i = 0; i < 8; i++)
            {
                var p = Around(rng, Ruins + new Vector2(25f, 40f), 30f);
                Place("CrystalCluster", parent, p.x, p.y, Range(rng, 0f, 360f), Range(rng, 0.7f, 1.4f), Collide.Mesh, sink: 0.3f);
            }
            for (int i = 0; i < 6; i++)
            {
                var p = Around(rng, Ruins, 70f);
                Place("TreeDead", parent, p.x, p.y, Range(rng, 0f, 360f), Range(rng, 0.9f, 1.3f), Collide.Trunk, sink: 0.3f, trunkRadius: 0.3f);
            }

            // Brother Aldous's shrine on the hill above the ruins.
            var shrine = Group("Shrine");
            Place("Cottage", shrine, Shrine.x, Shrine.y, 150f, 0.9f, sink: 0.3f);
            Place("RuinPillar", shrine, Shrine.x + 7f, Shrine.y - 6f, 0f, 0.8f);
            Place("RuinPillar", shrine, Shrine.x - 7f, Shrine.y - 8f, 0f, 0.8f);
            Place("LanternPost", shrine, Shrine.x + 4f, Shrine.y - 9f, 200f, 1f, Collide.Trunk, trunkRadius: 0.15f);
            Place("LanternPost", shrine, Shrine.x - 4f, Shrine.y - 10f, 160f, 1f, Collide.Trunk, trunkRadius: 0.15f);
        }

        // ------------------------------------------------------------------ creatures

        private const int MireFrog = 1, DuskWolf = 2, ShardBandit = 3, BanditShardcaster = 4, TrainingDummy = 5;
        private const int BristlebackBoar = 6, Mireling = 7, MirelingOracle = 8, GreywoodSpider = 9;
        private const int CaptainEdda = 10, OldTamsin = 11, QuartermasterHolt = 12, InnkeeperMarla = 13, FarmerTenney = 14;
        private const int BrannocIronvein = 15, ScoutRennick = 16, FishermanPell = 17, BrotherAldous = 18, SmithGerta = 19;
        private const int GreywoodBear = 20, DrownedDead = 21, Shardling = 22, LieutenantVorn = 23, Gloomjaw = 24, MireSnapper = 25;

        /// <summary>Spawn markers (exported to spawns.json). EditorOnly: they are not part of player builds.</summary>
        private static void CreateSpawns(Terrain terrain, System.Random rng)
        {
            var parent = new GameObject("CreatureSpawns") { tag = "EditorOnly" }.transform;
            void Spawn(int creature, float x, float z, float yaw, float wander = 0f)
            {
                var go = new GameObject("Spawn " + creature) { tag = "EditorOnly" };
                go.transform.SetParent(parent);
                go.transform.SetPositionAndRotation(new Vector3(x, GroundAt(x, z), z), Quaternion.Euler(0f, yaw, 0f));
                var marker = go.AddComponent<CreatureSpawnPoint>();
                marker.creatureId = creature;
                marker.wanderRadius = wander;
            }
            void Pack(int creature, Vector2 center, float radius, int count, float wander, Func<Vector2, bool> ok = null)
            {
                for (int placed = 0, tries = 0; placed < count && tries < count * 60; tries++)
                {
                    var p = Around(rng, center, radius);
                    if (ok != null && !ok(p)) continue;
                    if (DistanceToRoad(p) < 6f || LakeDistance(p.x, p.y) < 0.95f && creature != MireFrog) continue;
                    Spawn(creature, p.x, p.y, Range(rng, 0f, 360f), wander);
                    placed++;
                }
            }
            float vx = Village.x, vz = Village.y;

            // Mirewatch: people at their work, facing whoever comes to them.
            Spawn(CaptainEdda, vx + 20f, vz - 23f, 315f);
            Spawn(OldTamsin, vx - 20f, vz - 20f, 60f);
            Spawn(QuartermasterHolt, vx + 13f, vz - 2f, 270f);
            Spawn(InnkeeperMarla, vx + 2f, vz + 19f, 180f);
            Spawn(SmithGerta, vx - 15f, vz - 5f, 100f);
            Spawn(FishermanPell, 3f, -70f, 200f);
            Spawn(TrainingDummy, vx + 30f, vz - 31f, 315f);
            Spawn(TrainingDummy, vx + 34f, vz - 27f, 300f);
            Spawn(TrainingDummy, vx + 35f, vz - 21f, 285f);

            // Around the village.
            Spawn(FarmerTenney, Farm.x + 3f, Farm.y + 3f, 120f);
            Spawn(ScoutRennick, RennickTower.x + 4f, RennickTower.y - 6f, 140f);
            Spawn(BrannocIronvein, BrannocCamp.x - 1f, BrannocCamp.y - 4f, 200f);
            Spawn(BrotherAldous, Shrine.x, Shrine.y - 6f, 160f);

            // Levels 1-3: frogs on the south shore, boars in the fields.
            Pack(MireFrog, new Vector2(-70f, -62f), 30f, 6, 8f, p => Mathf.Abs(p.x) > 12f);
            Pack(MireFrog, new Vector2(70f, -60f), 30f, 6, 8f, p => Mathf.Abs(p.x) > 12f);
            Pack(BristlebackBoar, Farm + new Vector2(-10f, -20f), 85f, 14, 10f, p => Vector2.Distance(p, Farm) > 25f);

            // Levels 3-5: Reedfen's mirelings and their oracles; wolves at the Greywood's edge.
            Pack(Mireling, Reedfen + new Vector2(-20f, -20f), 110f, 16, 6f, p => Vector2.Distance(p, OracleStone) > 20f);
            Pack(MirelingOracle, OracleStone, 18f, 5, 3f, p => Vector2.Distance(p, OracleStone) > 7f);
            Pack(DuskWolf, new Vector2(-200f, 150f), 70f, 13, 10f);

            // Levels 5-7: spiders and bears deeper in the wood, snappers in the marsh.
            Pack(GreywoodSpider, new Vector2(-320f, 270f), 80f, 13, 8f);
            Pack(GreywoodBear, new Vector2(-360f, 390f), 60f, 8, 10f);
            // Snappers lie in the shallows (deeper water would hide them).
            Pack(MireSnapper, Reedfen + new Vector2(40f, 90f), 60f, 10, 8f, p => HeightAt(p.x, p.y) > WaterLevel - 0.35f);

            // Levels 5-8: the bandits of Shardscar.
            Pack(ShardBandit, BanditCamp, 12f, 7, 3f, p => Vector2.Distance(p, BanditCamp) > 3f);
            Spawn(BanditShardcaster, BanditCamp.x + 2f, BanditCamp.y + 10f, 200f);
            Spawn(BanditShardcaster, BanditCamp.x - 3f, BanditCamp.y + 11f, 210f);
            Pack(ShardBandit, Quarry, 40f, 6, 4f);
            Pack(BanditShardcaster, Quarry, 30f, 2, 0f);
            Spawn(LieutenantVorn, Quarry.x + 2f, Quarry.y + 3f, 225f);

            // Levels 8-10: the drowned dead and the shardlings of the ruins; Gloomjaw in the lagoon.
            Pack(DrownedDead, Ruins, 55f, 14, 6f);
            Pack(Shardling, Ruins + new Vector2(25f, 40f), 30f, 8, 5f);
            Spawn(Gloomjaw, Lagoon.x, Lagoon.y, 200f, 4f);
        }

        // ------------------------------------------------------------------ scene objects

        private static Material CreateMaterial(string path, Color color, string shader = "Universal Render Pipeline/Lit")
        {
            var material = new Material(Shader.Find(shader)) { color = color };
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void SetupLightingAndFog()
        {
            var sun = Object.FindAnyObjectByType<Light>();
            sun.name = "Sun";
            sun.transform.rotation = Quaternion.Euler(46f, -35f, 0f);
            sun.color = new Color(1f, 0.94f, 0.84f);
            sun.intensity = 1.4f;
            sun.shadows = LightShadows.Soft;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.64f, 0.72f, 0.78f);
            RenderSettings.fogStartDistance = 140f;
            RenderSettings.fogEndDistance = 620f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.63f, 0.74f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.48f, 0.45f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.23f, 0.19f);
        }

        private static void CreateMapInfo(Terrain terrain)
        {
            var map = new GameObject("Map");
            var info = map.AddComponent<MapInfo>();
            info.mapId = 1;
            info.mapName = "Hollowmere";
            info.waterLevel = WaterLevel;
            info.music = "music_hollowmere";
            info.ambience = "amb_wilds";
            var spawn = new GameObject("SpawnPoint").transform;
            spawn.SetParent(map.transform);
            spawn.position = new Vector3(Village.x, GroundAt(Village.x, Village.y - 8f), Village.y - 8f);
            info.spawnPoint = spawn;
            // Named places, for the minimap and "entering" messages. The first match wins.
            info.areas = new[]
            {
                new MapArea { name = "Mirewatch", center = Village, radius = VillageRadius + 8f, music = "music_mirewatch", ambience = "amb_village" },
                new MapArea { name = "Westbrook", center = Farm, radius = 110f },
                new MapArea { name = "The Old Mill", center = OldMill, radius = 30f },
                new MapArea { name = "Reedfen", center = Reedfen, radius = 160f, ambience = "amb_marsh" },
                new MapArea { name = "Rennick's Watch", center = RennickTower, radius = 25f, ambience = "amb_forest" },
                new MapArea { name = "The Greywood", center = Greywood, radius = 240f, ambience = "amb_forest" },
                new MapArea { name = "Ironvein Camp", center = BrannocCamp, radius = 25f },
                new MapArea { name = "Shardscar", center = Shardscar, radius = 200f },
                new MapArea { name = "Gloomjaw's Lagoon", center = Lagoon, radius = 55f, music = "music_drowned", ambience = "amb_ruins" },
                new MapArea { name = "The Drowned Ruins", center = Ruins, radius = 85f, music = "music_drowned", ambience = "amb_ruins" },
                new MapArea { name = "Shrine of the Still Water", center = Shrine, radius = 28f, music = "music_drowned", ambience = "amb_ruins" },
                new MapArea { name = "The Mere", center = LakeCenter, radius = 150f, ambience = "amb_marsh" },
            };
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
