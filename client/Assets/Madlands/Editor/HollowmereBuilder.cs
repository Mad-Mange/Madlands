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
        /// <summary>The boss's lair past the quarry: a glowing bowl of slag (Ruukhar the Slagborn).</summary>
        public static readonly Vector2 CinderCut = new Vector2(365f, 370f);
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
            // Each part draws its own random numbers, so changing one never moves what the others place.
            CreateVillage(terrain);
            CreateFarms(terrain, new System.Random(2026));
            CreateWilds(terrain, new System.Random(2027));
            CreateRockFormations(new System.Random(2031));
            CreateCamps(terrain, new System.Random(2028));
            CreateRuins(terrain, new System.Random(2029));
            CreateCinderCut();
            PaintUnderProps(terrain);
            PlantGroundCover(terrain, new System.Random(2032));
            CreateSpawns(terrain, new System.Random(2030));
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

        /// <summary>
        /// Steps a height into flat ledges and steep rises (the last <paramref name="rise"/> of each
        /// <paramref name="step"/> meters): cliff bands painted as rock, with grass on the ledges.
        /// </summary>
        private static float Terrace(float v, float step, float rise)
        {
            float t = v / step, i = Mathf.Floor(t);
            return (i + Smooth01((t - i - (1f - rise)) / rise)) * step;
        }

        /// <summary>Rolling ground over the valley floor: broad swells with knolls on them (meters).</summary>
        private static float Swells(float x, float z)
        {
            float broad = Smooth01(Noise(x, z, 0.005f, 1f, 41f) * 1.4f + 0.45f);
            float knolls = Mathf.Max(0f, Noise(x, z, 0.016f, 1f, 47f) + 0.1f);
            return broad * 7f + knolls * knolls * 9f;
        }

        /// <summary>
        /// The mountains closing the valley: their height over the valley floor. Some flanks are stepped into bands
        /// of cliff with grassy ledges between them (the ledges wander up and down), others stay smooth.
        /// </summary>
        private static float Mountains(float x, float z)
        {
            float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) + Noise(x, z, 0.008f, 28f, 23f);
            float rim = Mathf.Max(0f, edge - 380f) / 130f;
            float m = rim * rim * 95f + Smooth01((edge - 390f) / 70f) * (Noise(x, z, 0.02f, 14f, 17f) + Noise(x, z, 0.07f, 3f, 29f));
            float cliffs = Smooth01(Noise(x, z, 0.006f, 1f, 73f) * 2.2f + 0.55f);
            float stepped = Terrace(m + Noise(x, z, 0.012f, 7f, 71f) + Noise(x, z, 0.04f, 1.5f, 75f), 11f, 0.45f);
            return Mathf.Lerp(m, stepped, Smooth01((m - 5f) / 12f) * cliffs);
        }

        /// <summary>1 on a road, fading to 0 thirty meters from it.</summary>
        private static float RoadNear(Vector2 p) => 1f - Smooth01((DistanceToRoad(p) - 4f) / 26f);

        /// <summary>The ground before the places are leveled into it.</summary>
        private static float RawHeight(float x, float z)
        {
            var p = new Vector2(x, z);
            float road = RoadNear(p);
            float h = Ground + Noise(x, z, 0.004f, 3.5f) + Noise(x, z, 0.015f, 1.2f, 3f) + Noise(x, z, 0.06f, 0.25f, 7f);
            // Swells and knolls everywhere, gentler where a road runs and low around the drowned ruins.
            h += Swells(x, z) * (1f - 0.7f * road) * (1f - Influence(x, z, Ruins, 140f));
            // The Greywood rolls; Shardscar rises into rocky hills stepped into ledges, around a quarry pit cut in benches.
            h += Influence(x, z, Greywood, 240f) * (6f + Noise(x, z, 0.013f, 7f, 9f));
            float scar = Influence(x, z, Shardscar, 210f) * (16f + Noise(x, z, 0.02f, 5f, 13f))
                         - (1f - Smooth01(Vector2.Distance(p, Quarry) / 70f)) * 13f;
            h += Mathf.Lerp(scar, Terrace(scar + Noise(x, z, 0.04f, 1.5f, 51f), 5f, 0.3f), Smooth01((scar - 3f) / 5f) * (1f - road));
            h -= (1f - Smooth01(Vector2.Distance(p, CinderCut) / 32f)) * 6f;
            return h + Mountains(x, z);
        }

        /// <summary>Camps and landmarks stand on level ground: flat out to the radius at the height of the center, blending out to twice it.</summary>
        private static readonly (Vector2 center, float radius)[] Pads =
        {
            (RennickTower, 16f), (BrannocCamp, 16f), (BanditCamp, 24f), (Shrine, 16f), (OldMill, 14f),
        };

        private static float[] _padHeights;

        public static float HeightAt(float x, float z)
        {
            float h = RawHeight(x, z);
            if (_padHeights == null)
            {
                _padHeights = new float[Pads.Length];
                for (int i = 0; i < Pads.Length; i++) _padHeights[i] = RawHeight(Pads[i].center.x, Pads[i].center.y);
            }
            for (int i = 0; i < Pads.Length; i++)
            {
                float f = Vector2.Distance(new Vector2(x, z), Pads[i].center) / Pads[i].radius;
                if (f < 2f) h = Mathf.Lerp(_padHeights[i], h, Smooth01(f - 1f));
            }

            // Flat ground for the village out past its gates; the farmyard and the fields lie level, the land rolling gently between them.
            h = Mathf.Lerp(h, Ground + 1.5f, 1f - Smooth01((Vector2.Distance(new Vector2(x, z), Village) - VillageRadius - 6f) / 32f));
            float farm = Mathf.Max(Mathf.Clamp01(Influence(x, z, Farm, 170f) * 1.6f), 1f - Smooth01((FieldDistance(x, z) - 2f) / 14f));
            h = Mathf.Lerp(h, Ground + 0.5f + Noise(x, z, 0.01f, 0.6f), farm);

            // Reedfen: just above and below the water, so pools appear by themselves.
            float marsh = Mathf.Clamp01(Influence(x, z, Reedfen, 160f) * 1.6f);
            h = Mathf.Lerp(h, WaterLevel + 0.25f + Noise(x, z, 0.035f, 0.75f, 21f), marsh);
            // The lagoon by the ruins, where something large sleeps.
            float lagoon = Vector2.Distance(new Vector2(x, z), Lagoon) / 55f * (1f + Noise(x, z, 0.03f, 0.18f, 99f));
            if (lagoon < 1.4f) h = Mathf.Lerp(WaterLevel - 0.9f, h, Smooth01((lagoon - 0.7f) / 0.7f));

            // The mere: a wide shallow bowl, its beach shelving evenly into the water (a shore this steep stays where it
            // is when the terrain is drawn coarser far away).
            float shore = (LakeDistance(x, z) - 1f) * 140f;
            if (shore < 40f)
            {
                float beach = WaterLevel + 0.1f * Mathf.Clamp(shore, -3f, 4f) - 0.6f * Smooth01((-shore - 3f) / 50f);
                h = Mathf.Lerp(beach, h, Smooth01((shore - 4f) / 36f));
            }
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

        /// <summary>The terrain layers, in the order of <see cref="CreateTerrain"/>'s list.</summary>
        private const int GrassLayer = 0, MeadowLayer = 1, DirtLayer = 2, RockLayer = 3, MudLayer = 4, ForestLayer = 5, ShingleLayer = 6, SnowLayer = 7, Layers = 8;
        private const int GroundResolution = 1024;

        /// <summary>The sun comes from the south-east, 40 degrees up.</summary>
        private static readonly Quaternion SunRotation = Quaternion.Euler(40f, -35f, 0f);

        private static Terrain CreateTerrain()
        {
            var data = new TerrainData
            {
                heightmapResolution = Resolution,
                alphamapResolution = GroundResolution,
                baseMapResolution = 1024,
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

            // Painted ground (GroundPainter): lush grass, a lighter sunlit meadow, earth, rock, mud, the forest floor,
            // the mere's shingle beach and snow on the peaks.
            string dir = TerrainDir;
            data.terrainLayers = new[]
            {
                GroundPainter.Layer(dir, "Grass", 11, 6f, 0.05f, 0.5f, c => GroundPainter.Grass(c, "#3c5f27", "#557a31", "#30501f", "#6e9440", "#7f9a45", 9000)),
                GroundPainter.Layer(dir, "Meadow", 12, 7f, 0.05f, 0.5f, c => GroundPainter.Grass(c, "#62813a", "#7f9846", "#4f6d2c", "#a3b25a", "#b9ad62", 8000)),
                GroundPainter.Layer(dir, "Dirt", 22, 4f, 0.1f, 0.8f, c => GroundPainter.Dirt(c, "#6a5236", "#8a6d48", "#5f4b32", "#a39176", 320)),
                GroundPainter.Layer(dir, "Rock", 33, 9f, 0.12f, 1f, c => GroundPainter.Rock(c, "#615e59", "#948f86", "#3e3b38", "#5c6e3c")),
                GroundPainter.Layer(dir, "Mud", 44, 5f, 0.35f, 0.6f, c => GroundPainter.Mud(c, "#3a3524", "#504830", "#332f21", "#6c6450")),
                GroundPainter.Layer(dir, "Forest", 55, 5f, 0.05f, 0.7f, c => GroundPainter.Forest(c, "#2e3c22", "#46542c", "#40612a",
                    new[] { "#7a5a32", "#8c6a34", "#6b6a2e", "#a0662e", "#5a4a2c" }, 2600)),
                GroundPainter.Layer(dir, "Shingle", 66, 4f, 0.15f, 0.8f, c => GroundPainter.Shingle(c, "#7d7564", "#9c937b",
                    new[] { "#8c8676", "#a8a090", "#6f6a60", "#938468", "#b4ab9a" }, 900)),
                GroundPainter.Layer(dir, "Snow", 77, 10f, 0.3f, 0.4f, c => GroundPainter.Snow(c, "#c8d4e0", "#f4f8fb")),
            };

            int ar = data.alphamapResolution;
            var alpha = new float[ar, ar, Layers];
            var weights = new float[Layers];
            var toSun = -(SunRotation * Vector3.forward);
            for (int y = 0; y < ar; y++)
            {
                for (int x = 0; x < ar; x++)
                {
                    float nx = (x + 0.5f) / ar, nz = (y + 0.5f) / ar;
                    GroundCover(data, -Size / 2 + nx * Size, -Size / 2 + nz * Size, nx, nz, toSun, weights);
                    for (int i = 0; i < Layers; i++) alpha[y, x, i] = weights[i];
                }
            }
            data.SetAlphamaps(0, 0, alpha);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.position = new Vector3(-Size / 2, 0f, -Size / 2);
            var terrain = go.GetComponent<Terrain>();
            var material = CreateMaterial(TerrainDir + "/TerrainLit.mat", Color.white, "Universal Render Pipeline/Terrain/Lit");
            // The layers' normal maps only show with the keyword (the material inspector would set it).
            material.EnableKeyword("_NORMALMAP");
            terrain.materialTemplate = material;
            terrain.heightmapPixelError = 3f;
            terrain.basemapDistance = 500f;
            terrain.drawInstanced = true;
            return terrain;
        }

        /// <summary>How much of each terrain layer covers the ground at a point (weights summing to 1).</summary>
        private static void GroundCover(TerrainData data, float x, float z, float nx, float nz, Vector3 toSun, float[] w)
        {
            var p = new Vector2(x, z);
            float height = HeightAt(x, z);
            float steep = data.GetSteepness(nx, nz);
            // Bare rock on cliffs and high up the mountains, in the quarry and the Cinder Cut; snow on the high ledges.
            float rock = Mathf.Max(Smooth01((steep - 30f) / 9f), Smooth01((height - 100f - Noise(x, z, 0.02f, 10f, 79f)) / 26f) * 0.6f);
            rock = Mathf.Max(rock, Mathf.Clamp01(Influence(x, z, Quarry, 80f) * 1.5f));
            rock = Mathf.Max(rock, Mathf.Clamp01(Influence(x, z, CinderCut, 42f) * 1.8f));
            float snow = Smooth01((height - 112f - Noise(x, z, 0.03f, 8f, 81f)) / 6f) * (1f - Smooth01((steep - 36f) / 8f));
            // A shingle beach around the mere, out into its shallows (not where it meets the marsh); mud wherever else it is wet.
            float shore = LakeDistance(x, z);
            float beach = (1f - Smooth01((shore - 1.035f - Noise(x, z, 0.05f, 0.03f, 83f)) / 0.03f)) * Smooth01((shore - 0.55f) / 0.2f)
                          * (1f - Mathf.Clamp01(Influence(x, z, Reedfen, 170f) * 2.2f));
            float mud = Smooth01((WaterLevel + 1.2f - height) / 1.2f);
            // Earth on the roads, the village square, the plowed fields and around the camps.
            float road = 1f - Smooth01((DistanceToRoad(p) - 2.2f) / 2f);
            float village = Influence(x, z, Village, VillageRadius - 5f) * 2f;
            float camps = 0f;
            foreach (var (center, radius) in Pads) camps = Mathf.Max(camps, Influence(x, z, center, radius * 0.8f) * 1.6f);
            camps *= Smooth01(Noise(x, z, 0.12f, 1f, 85f) + 0.7f);
            float dirt = Mathf.Clamp01(Mathf.Max(Mathf.Max(road, village), Mathf.Max(FieldMask(x, z), camps)));
            float forest = Mathf.Clamp01(Influence(x, z, Greywood, 230f) * 2f) * Smooth01(Noise(x, z, 0.03f, 1f, 3f) + 0.6f);
            // Lighter meadow in broad patches, on the slopes facing the sun and up on the swells.
            float sun = Vector3.Dot(data.GetInterpolatedNormal(nx, nz), toSun) - toSun.y;
            float meadow = Smooth01(Noise(x, z, 0.007f, 1f, 87f) * 1.7f + 0.4f + sun * 3f + (height - Ground - 5f) * 0.04f);

            // Each layer takes its share of what is left.
            float left = 1f;
            float Take(float amount)
            {
                float share = Mathf.Clamp01(amount) * left;
                left -= share;
                return share;
            }
            w[RockLayer] = Take(rock);
            w[SnowLayer] = Take(snow);
            w[ShingleLayer] = Take(beach);
            w[MudLayer] = Take(mud);
            w[DirtLayer] = Take(dirt);
            w[ForestLayer] = Take(forest);
            w[MeadowLayer] = Take(meadow);
            w[GrassLayer] = left;
        }

        /// <summary>
        /// Paints <paramref name="layer"/> over the ground in a soft disc, ragged at its rim (forest floor under a tree,
        /// trodden earth around a fire). The weights stay summed to 1; cliffs keep their rock.
        /// </summary>
        private static void Splash(float[,,] alpha, Vector2 center, float radius, int layer, float strength)
        {
            int res = alpha.GetLength(0);
            float cell = Size / res;
            int cx = Mathf.FloorToInt((center.x + Size / 2) / cell), cz = Mathf.FloorToInt((center.y + Size / 2) / cell);
            int r = Mathf.CeilToInt(radius / cell) + 1;
            for (int y = Mathf.Max(0, cz - r); y <= Mathf.Min(res - 1, cz + r); y++)
            {
                for (int x = Mathf.Max(0, cx - r); x <= Mathf.Min(res - 1, cx + r); x++)
                {
                    float wx = -Size / 2 + (x + 0.5f) * cell, wz = -Size / 2 + (y + 0.5f) * cell;
                    float d = Vector2.Distance(new Vector2(wx, wz), center) / radius + Noise(wx, wz, 0.4f, 0.18f, 89f);
                    float a = strength * (1f - Smooth01((d - 0.45f) / 0.55f)) * (1f - alpha[y, x, RockLayer]);
                    if (a <= 0f) continue;
                    for (int i = 0; i < Layers; i++) alpha[y, x, i] *= 1f - a;
                    alpha[y, x, layer] += a;
                }
            }
        }

        /// <summary>The farm fields: plowed rectangles west of the village.</summary>
        private static readonly (Vector2 center, Vector2 size, float yaw)[] Fields =
        {
            (new Vector2(-230f, -205f), new Vector2(44f, 26f), 8f),
            (new Vector2(-285f, -215f), new Vector2(38f, 28f), -6f),
            (new Vector2(-215f, -95f), new Vector2(40f, 24f), 15f),
            (new Vector2(-300f, -60f), new Vector2(36f, 30f), 25f),
        };

        /// <summary>Meters outside the nearest field (negative inside it).</summary>
        private static float FieldDistance(float x, float z)
        {
            float best = float.MaxValue;
            foreach (var (center, size, yaw) in Fields)
            {
                var local = Quaternion.Euler(0f, 0f, yaw) * new Vector3(x - center.x, z - center.y, 0f);
                best = Mathf.Min(best, Mathf.Max(Mathf.Abs(local.x) - size.x / 2, Mathf.Abs(local.y) - size.y / 2));
            }
            return best;
        }

        private static float FieldMask(float x, float z) => 1f - Smooth01(FieldDistance(x, z) / 1.5f);

        /// <summary>
        /// The water at <see cref="WaterLevel"/>: a <see cref="WaterSurface"/> builds its mesh over every place where
        /// the terrain dips under it (the mere, the marsh pools, the lagoon) when the map loads; here too, so the map
        /// render shows it. The Madlands/Water shader colors it by depth and foams along the shore.
        /// </summary>
        private static void CreateWater()
        {
            var water = new GameObject("Water");
            water.transform.position = new Vector3(0f, WaterLevel, 0f);
            water.AddComponent<MeshFilter>();
            var renderer = water.AddComponent<MeshRenderer>();
            var material = new Material(Shader.Find("Madlands/Water"));
            string materialPath = MaterialDir + "/Water.mat";
            AssetDatabase.DeleteAsset(materialPath);
            AssetDatabase.CreateAsset(material, materialPath);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            water.AddComponent<WaterSurface>().Build();
        }

        // ------------------------------------------------------------------ props

        private enum Collide { None, Mesh, Trunk }

        private static Terrain _terrain;

        private static float GroundAt(float x, float z) => _terrain.SampleHeight(new Vector3(x, 0f, z)) + _terrain.GetPosition().y;

        /// <summary>Turns a placed prop to lie along the slope of the ground under it (pools, not things that stand).</summary>
        private static void LieOnGround(GameObject prop)
        {
            var p = prop.transform.position - _terrain.GetPosition();
            var size = _terrain.terrainData.size;
            var normal = _terrain.terrainData.GetInterpolatedNormal(p.x / size.x, p.z / size.z);
            prop.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal) * prop.transform.rotation;
        }

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

        private static bool FreeForScatter(Vector2 p, float roadMargin = 5f, float bound = 470f)
        {
            if (Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) > bound) return false;
            if (LakeDistance(p.x, p.y) < 1.05f) return false;
            if (DistanceToRoad(p) < roadMargin) return false;
            if (FieldMask(p.x, p.y) > 0.01f) return false;
            foreach (var (center, radius) in Clearings)
                if (Vector2.Distance(p, center) < radius) return false;
            return true;
        }

        private static bool Dry(Vector2 p) => HeightAt(p.x, p.y) > WaterLevel + 0.3f;

        /// <summary>The way down at a point (unit, horizontal) and how steep it is there in degrees.</summary>
        private static Vector2 Downhill(Vector2 p, out float slope)
        {
            var gradient = new Vector2(HeightAt(p.x + 1f, p.y) - HeightAt(p.x - 1f, p.y), HeightAt(p.x, p.y + 1f) - HeightAt(p.x, p.y - 1f)) / 2f;
            slope = Mathf.Atan(gradient.magnitude) * Mathf.Rad2Deg;
            return gradient.sqrMagnitude > 1e-8f ? -gradient.normalized : Vector2.up;
        }

        private static float Slope(Vector2 p)
        {
            Downhill(p, out float slope);
            return slope;
        }

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

            // Homes around the square, facing it: four timber grove houses on the square, cottages further out.
            Place("GroveHouse", parent, vx - 30f, vz - 6f, 90f, sink: 0.2f);
            Place("GroveHouse", parent, vx - 28f, vz + 14f, 110f, sink: 0.2f);
            Place("GroveHouse", parent, vx + 30f, vz - 6f, 270f, sink: 0.2f);
            Place("GroveHouse", parent, vx + 28f, vz + 14f, 250f, sink: 0.2f);
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
                if (!FreeForScatter(p, 4f) || !Dry(p) || Slope(p) > 30f) continue;
                bool pine = rng.NextDouble() < 0.6;
                string model = pine ? (rng.NextDouble() < 0.5 ? "TreePine1" : "TreePine2") : (rng.NextDouble() < 0.5 ? "TreeOak1" : "TreeOak2");
                Place(model, trees, p.x, p.y, Range(rng, 0f, 360f), Range(rng, 0.8f, 1.3f), Collide.Trunk, sink: 0.2f, trunkRadius: pine ? 0.3f : 0.38f);
                placed++;
                // Glowcaps at the feet of the trees: gatherable for Old Tamsin's quest (drawn by the client, not the scene).
                if (rng.NextDouble() < 0.12) ObjectSpawn(GlowcapMushrooms, p.x + 2f, p.y + 1f, Range(rng, 0f, 360f));
                if (rng.NextDouble() < 0.05) Place("Log", plants, p.x - 3f, p.y + 2f, Range(rng, 0f, 360f), 1f, Collide.Mesh, sink: 0.15f);
                if (rng.NextDouble() < 0.05) Place("Stump", plants, p.x + 3f, p.y - 2f, Range(rng, 0f, 360f), 1f, Collide.Mesh);
            }

            // Copses over the rest of the valley: oaks and a pine or two together, bushes at their edge; lone trees between them.
            bool Open(Vector2 p) => Influence(p.x, p.y, Greywood, 240f) <= 0f && Influence(p.x, p.y, Reedfen, 150f) <= 0.3f;
            for (int copses = 0, tries = 0; copses < 36 && tries < 4000; tries++)
            {
                var c = new Vector2(Range(rng, -440f, 440f), Range(rng, -440f, 440f));
                if (!Open(c) || !FreeForScatter(c, 12f) || !Dry(c) || Slope(c) > 22f) continue;
                int count = rng.Next(3, 8);
                float spread = 5f + count * 1.6f;
                for (int i = 0; i < count; i++)
                {
                    var p = Around(rng, c, spread);
                    if (!FreeForScatter(p) || !Dry(p) || Slope(p) > 26f) continue;
                    bool pine = rng.NextDouble() < 0.25;
                    Place(pine ? "TreePine1" : rng.NextDouble() < 0.5 ? "TreeOak1" : "TreeOak2", trees, p.x, p.y, Range(rng, 0f, 360f), Range(rng, 0.8f, 1.35f),
                        Collide.Trunk, sink: 0.2f, trunkRadius: pine ? 0.3f : 0.35f);
                }
                for (int i = rng.Next(2, 6); i > 0; i--)
                {
                    var p = Around(rng, c, spread + 4f);
                    if (!FreeForScatter(p, 3f) || !Dry(p)) continue;
                    Place(rng.NextDouble() < 0.5 ? "Bush1" : "Bush2", plants, p.x, p.y, Range(rng, 0f, 360f), Range(rng, 0.7f, 1.3f), Collide.None, sink: 0.1f);
                }
                if (rng.NextDouble() < 0.3)
                {
                    var p = Around(rng, c, spread);
                    if (FreeForScatter(p) && Dry(p)) Place(rng.NextDouble() < 0.5 ? "Log" : "Stump", plants, p.x, p.y, Range(rng, 0f, 360f), 1f, Collide.Mesh, sink: 0.15f);
                }
                copses++;
            }
            for (int placed = 0, tries = 0; placed < 50 && tries < 4000; tries++)
            {
                var p = new Vector2(Range(rng, -460f, 460f), Range(rng, -460f, 460f));
                if (!Open(p) || !FreeForScatter(p) || !Dry(p) || Slope(p) > 26f) continue;
                string model = rng.NextDouble() < 0.7 ? (rng.NextDouble() < 0.5 ? "TreeOak1" : "TreeOak2") : "TreePine1";
                Place(model, trees, p.x, p.y, Range(rng, 0f, 360f), Range(rng, 0.8f, 1.3f), Collide.Trunk, sink: 0.2f, trunkRadius: 0.35f);
                placed++;
            }
            // Pines climb the lower mountainsides, thinning out with height and keeping off the cliffs.
            for (int placed = 0, tries = 0; placed < 320 && tries < 20000; tries++)
            {
                var p = new Vector2(Range(rng, -500f, 500f), Range(rng, -500f, 500f));
                float m = Mountains(p.x, p.y);
                if (m < 1f || rng.NextDouble() > 1.15f - m / 40f) continue;
                if (!FreeForScatter(p, 5f, 500f) || !Dry(p) || Slope(p) > 30f) continue;
                Place(rng.NextDouble() < 0.5 ? "TreePine1" : "TreePine2", trees, p.x, p.y, Range(rng, 0f, 360f), Range(rng, 0.75f, 1.25f), Collide.Trunk, sink: 0.3f,
                    trunkRadius: 0.3f);
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
            // Water lilies float in the sheltered shallows of the mere and the marsh pools (not at the dock or in the lagoon).
            for (int placed = 0, tries = 0; placed < 70 && tries < 20000; tries++)
            {
                var p = placed % 3 == 0 ? Around(rng, Reedfen, 150f) : new Vector2(Range(rng, -170f, 170f), Range(rng, -60f, 180f));
                float depth = WaterLevel - HeightAt(p.x, p.y);
                if (depth < 0.25f || depth > 0.8f || Vector2.Distance(p, new Vector2(0f, -64f)) < 14f || Vector2.Distance(p, Lagoon) < 70f) continue;
                var lily = Place("LilyPads", plants, p.x, p.y, Range(rng, 0f, 360f), Range(rng, 0.8f, 1.4f), Collide.None, sink: 0f);
                lily.transform.position = new Vector3(p.x, WaterLevel + 0.01f, p.y);
                placed++;
            }
        }

        /// <summary>
        /// Rock that breaks the skyline: cliffs at the foot of the mountains facing down into the valley, spires and
        /// outcrops on Shardscar's ledges, outcrops crowning knolls in the open valley and mossy ones in the Greywood.
        /// </summary>
        private static void CreateRockFormations(System.Random rng)
        {
            var parent = Group("RockFormations");
            var placed = new List<Vector2>();
            bool Spaced(Vector2 p, float spacing)
            {
                foreach (var q in placed)
                    if (Vector2.Distance(p, q) < spacing) return false;
                return true;
            }
            void Formation(string model, Vector2 p, float yaw, float scale, float sink)
            {
                Place(model, parent, p.x, p.y, yaw, scale, Collide.Mesh, sink: sink);
                placed.Add(p);
            }
            float Facing(Vector2 dir) => Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;

            for (int count = 0, tries = 0; count < 48 && tries < 20000; tries++)
            {
                var p = new Vector2(Range(rng, -500f, 500f), Range(rng, -500f, 500f));
                float m = Mountains(p.x, p.y);
                if (m < 2f || m > 34f || !FreeForScatter(p, 10f, 500f) || !Spaced(p, 26f)) continue;
                var down = Downhill(p, out float slope);
                if (slope < 14f || slope > 50f) continue;
                Formation(rng.NextDouble() < 0.5 ? "Cliff1" : "Cliff2", p, Facing(down) + Range(rng, -20f, 20f), Range(rng, 0.8f, 1.4f), 1.2f);
                count++;
            }
            for (int count = 0, tries = 0; count < 16 && tries < 4000; tries++)
            {
                var p = Around(rng, Shardscar, 190f);
                if (Influence(p.x, p.y, Shardscar, 210f) < 0.25f || !FreeForScatter(p, 9f) || !Spaced(p, 22f) || Vector2.Distance(p, CinderCut) < 45f) continue;
                var down = Downhill(p, out float slope);
                if (slope > 35f) continue;
                string model = rng.NextDouble() < 0.35 ? "RockSpire" : rng.NextDouble() < 0.5 ? "Outcrop1" : "Outcrop2";
                Formation(model, p, Facing(down) + Range(rng, -40f, 40f), Range(rng, 0.8f, 1.3f), 0.5f);
                count++;
            }
            for (int count = 0, tries = 0; count < 14 && tries < 8000; tries++)
            {
                var p = new Vector2(Range(rng, -430f, 430f), Range(rng, -430f, 430f));
                if (Swells(p.x, p.y) < 7.5f || Influence(p.x, p.y, Shardscar, 210f) > 0.2f || !FreeForScatter(p, 14f) || !Dry(p) || !Spaced(p, 70f)) continue;
                var down = Downhill(p, out float slope);
                if (slope > 24f) continue;
                Formation(rng.NextDouble() < 0.5 ? "Outcrop1" : "Outcrop2", p, Facing(down) + Range(rng, -30f, 30f), Range(rng, 0.8f, 1.2f), 0.5f);
                count++;
            }
            for (int count = 0, tries = 0; count < 7 && tries < 3000; tries++)
            {
                var p = Around(rng, Greywood, 200f);
                if (!FreeForScatter(p, 8f) || !Dry(p) || !Spaced(p, 40f) || Slope(p) > 25f) continue;
                Formation("Outcrop2", p, Range(rng, 0f, 360f), Range(rng, 0.7f, 1.0f), 0.5f);
                count++;
            }
        }

        /// <summary>
        /// Ground under what stands on it: forest floor under every tree and bush, trodden earth around fires and
        /// tents, scree at the foot of the rocks.
        /// </summary>
        private static void PaintUnderProps(Terrain terrain)
        {
            var data = terrain.terrainData;
            var alpha = data.GetAlphamaps(0, 0, data.alphamapResolution, data.alphamapResolution);
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                var p = new Vector2(t.position.x, t.position.z);
                float s = t.lossyScale.x;
                switch (t.name)
                {
                    case "TreeOak1": case "TreeOak2": Splash(alpha, p, 3.4f * s, ForestLayer, 0.8f); break;
                    case "TreePine1": case "TreePine2": Splash(alpha, p, 2.8f * s, ForestLayer, 0.75f); break;
                    case "Bush1": case "Bush2": Splash(alpha, p, 1.4f * s, ForestLayer, 0.6f); break;
                    case "Campfire": Splash(alpha, p, 3.5f * s, DirtLayer, 0.9f); break;
                    case "Tent": Splash(alpha, p, 3.2f * s, DirtLayer, 0.55f); break;
                    case "Rock1": case "Rock2": case "Boulder": Splash(alpha, p, 1.6f * s * (t.name == "Boulder" ? 2.4f : 1f), DirtLayer, 0.45f); break;
                    case "Cliff1": case "Cliff2": case "Outcrop1": case "Outcrop2": case "RockSpire": Splash(alpha, p, 7f * s, DirtLayer, 0.5f); break;
                }
            }
            data.SetAlphamaps(0, 0, alpha);
            EditorUtility.SetDirty(data);
        }

        /// <summary>Ground cover the terrain draws itself near the camera, GPU instanced: the Art/Ground models.</summary>
        private static readonly string[] Cover = { "GrassTuft", "GrassTuftMeadow", "GrassTall", "FlowersWhite", "FlowersYellow", "FlowersBlue", "Fern", "Pebbles" };
        private const int Tufts = 0, MeadowTufts = 1, TallGrass = 2, WhiteFlowers = 3, YellowFlowers = 4, BlueFlowers = 5, Ferns = 6, Stones = 7;

        /// <summary>
        /// Grass tufts in clumps over the grass and the meadow, tall grass at the water's edge and along the fields,
        /// flowers in drifts, ferns on the forest floor, pebbles at the edges of the roads and below the rocks; never
        /// under anything that stands on the ground, in water, or on the cliffs.
        /// </summary>
        private static void PlantGroundCover(Terrain terrain, System.Random rng)
        {
            var data = terrain.terrainData;
            const int res = GroundResolution;
            data.SetDetailResolution(res, 32);
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            var prototypes = new DetailPrototype[Cover.Length];
            for (int i = 0; i < Cover.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Art/Ground/{Cover[i]}.fbx");
                if (prefab == null) throw new InvalidOperationException($"Missing ground cover {Cover[i]}: build it with art/blender/build.py");
                prototypes[i] = new DetailPrototype
                {
                    prototype = prefab,
                    usePrototypeMesh = true,
                    useInstancing = true,
                    renderMode = DetailRenderMode.VertexLit,
                    minWidth = i == Stones ? 0.6f : 0.8f,
                    maxWidth = i == Stones ? 1.6f : 1.25f,
                    minHeight = 0.8f,
                    maxHeight = i == Stones ? 1.4f : 1.3f,
                    noiseSpread = 0.4f,
                    healthyColor = Color.white,
                    dryColor = Color.white,
                    alignToGround = 0.6f,
                };
            }
            data.detailPrototypes = prototypes;

            // Cells under anything standing on the ground stay bare.
            var blocked = new bool[res, res];
            float cell = Size / res;
            void Block(Vector3 min, Vector3 max)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt((min.x + Size / 2) / cell)), x1 = Mathf.Min(res - 1, Mathf.FloorToInt((max.x + Size / 2) / cell));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((min.z + Size / 2) / cell)), z1 = Mathf.Min(res - 1, Mathf.FloorToInt((max.z + Size / 2) / cell));
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                        blocked[z, x] = true;
            }
            foreach (var collider in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (collider is TerrainCollider) continue;
                if (collider is CapsuleCollider capsule)
                {
                    var c = capsule.transform.position;
                    float r = capsule.radius + 0.4f;
                    Block(c - new Vector3(r, 0f, r), c + new Vector3(r, 0f, r));
                }
                else Block(collider.bounds.min, collider.bounds.max);
            }
            foreach (var name in new[] { "Campfire", "Dock", "Reeds", "SlagPool", "Cauldron", "Sacks" })
            {
                foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                {
                    if (t.name != name) continue;
                    foreach (var renderer in t.GetComponentsInChildren<Renderer>()) Block(renderer.bounds.min, renderer.bounds.max);
                }
            }

            var alpha = data.GetAlphamaps(0, 0, res, res);
            var layers = new int[Cover.Length][,];
            for (int i = 0; i < Cover.Length; i++) layers[i] = new int[res, res];
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    if (blocked[z, x]) continue;
                    float nx = (x + 0.5f) / res, nz = (z + 0.5f) / res;
                    float wx = -Size / 2 + nx * Size, wz = -Size / 2 + nz * Size;
                    float height = HeightAt(wx, wz);
                    if (height < WaterLevel + 0.12f || data.GetSteepness(nx, nz) > 34f) continue;
                    float grass = alpha[z, x, GrassLayer], meadow = alpha[z, x, MeadowLayer], forest = alpha[z, x, ForestLayer];
                    float dirt = alpha[z, x, DirtLayer], rock = alpha[z, x, RockLayer], shingle = alpha[z, x, ShingleLayer];
                    void Add(int layer, float density)
                    {
                        int count = Mathf.FloorToInt(density);
                        if (rng.NextDouble() < density - count) count++;
                        layers[layer][z, x] = Mathf.Min(count, 12);
                    }
                    // Grass grows in clumps and thins out between them.
                    float clump = Smooth01(Noise(wx, wz, 0.09f, 1f, 91f) + 0.55f);
                    Add(Tufts, grass * (0.5f + 2f * clump));
                    Add(MeadowTufts, meadow * (0.5f + 2f * clump));
                    // Tall grass on the wet ground at the water's edge, and along the edges of the fields.
                    float wet = Smooth01((height - WaterLevel) / 0.4f) * (1f - Smooth01((height - WaterLevel - 1.6f) / 0.8f));
                    float margin = 1f - Smooth01((Mathf.Abs(FieldDistance(wx, wz) - 3f) - 1f) / 1.5f);
                    Add(TallGrass, (wet * 0.6f * (1f - shingle) + margin * 0.7f) * (1f - dirt) * clump);
                    // Flowers in drifts, one kind to a drift, and a few strays anywhere on the grass.
                    float drift = Smooth01((Noise(wx, wz, 0.03f, 1f, 93f) - 0.18f) / 0.15f);
                    float kind = Noise(wx, wz, 0.011f, 1f, 95f);
                    Add(kind < -0.12f ? WhiteFlowers : kind < 0.15f ? YellowFlowers : BlueFlowers, (grass + meadow) * (drift * 0.8f + 0.03f));
                    Add(Ferns, forest * Smooth01(Noise(wx, wz, 0.06f, 1f, 97f) + 0.45f) * 0.5f);
                    // Pebbles at the edges of the roads and under the rocks, a few on the beach.
                    Add(Stones, dirt * (1f - dirt) * 1.1f + rock * (1f - rock) * 1.4f + shingle * 0.1f);
                }
            }
            for (int i = 0; i < Cover.Length; i++) data.SetDetailLayer(0, 0, i, layers[i]);
            // Changes to a saved TerrainData only reach the disk when it is marked dirty.
            EditorUtility.SetDirty(data);
            int total = 0;
            foreach (var layer in layers)
                foreach (int count in layer)
                    total += count;
            Debug.Log($"[HollowmereBuilder] Ground cover: {total} pieces");
            terrain.detailObjectDistance = 90f;
            terrain.detailObjectDensity = 1f;
        }

        // ------------------------------------------------------------------ the Cinder Cut

        /// <summary>
        /// Ruukhar's lair: a bowl past the quarry with pools of molten slag, glowing slag heaps on the rim and the
        /// skull of something enormous. Its own random numbers, built after the rest, so nothing else moves.
        /// </summary>
        private static void CreateCinderCut()
        {
            var rng = new System.Random(77);
            var lair = Group("CinderCut");
            // Clear the scattered trees, bushes and rocks out of the bowl first.
            foreach (var name in new[] { "Trees", "Plants", "Rocks" })
            {
                var group = GameObject.Find(name);
                if (group == null) continue;
                for (int i = group.transform.childCount - 1; i >= 0; i--)
                {
                    var child = group.transform.GetChild(i);
                    if (Vector2.Distance(new Vector2(child.position.x, child.position.z), CinderCut) < 36f) Object.DestroyImmediate(child.gameObject);
                }
            }
            // Slag pools around the middle (the fight needs the middle free), lying along the bowl's slope; heaps on the rim.
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI * 2f / 6f + Range(rng, -0.25f, 0.25f), r = Range(rng, 13f, 19f);
                LieOnGround(Place("SlagPool", lair, CinderCut.x + Mathf.Cos(a) * r, CinderCut.y + Mathf.Sin(a) * r, Range(rng, 0f, 360f), Range(rng, 0.8f, 1.3f),
                    Collide.None, sink: 0.05f));
            }
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2f / 10f + Range(rng, -0.2f, 0.2f), r = Range(rng, 24f, 30f);
                // Leave the way in from the quarry (south-west) open.
                if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 225f)) < 25f) continue;
                Place("SlagRock", lair, CinderCut.x + Mathf.Cos(a) * r, CinderCut.y + Mathf.Sin(a) * r, Range(rng, 0f, 360f), Range(rng, 1.0f, 1.8f), Collide.Mesh, sink: 0.3f);
            }
            Place("BeastSkull", lair, CinderCut.x + 9f, CinderCut.y + 14f, 200f, 1.6f, Collide.Mesh, sink: 0.6f);
            Place("CrystalCluster", lair, CinderCut.x - 14f, CinderCut.y + 10f, 40f, 1.2f, Collide.None, sink: 0.3f);
            Place("CrystalCluster", lair, CinderCut.x + 16f, CinderCut.y - 6f, 120f, 0.9f, Collide.None, sink: 0.3f);
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
        private const int RuukharTheSlagborn = 26;
        private const int GlowcapMushrooms = 1;

        /// <summary>A gatherable object (exported to objects.json): the server spawns it and the client draws it.</summary>
        private static void ObjectSpawn(int objectId, float x, float z, float yaw)
        {
            var parent = GameObject.Find("ObjectSpawns");
            if (parent == null) parent = new GameObject("ObjectSpawns") { tag = "EditorOnly" };
            var go = new GameObject("Object " + objectId) { tag = "EditorOnly" };
            go.transform.SetParent(parent.transform);
            go.transform.SetPositionAndRotation(new Vector3(x, GroundAt(x, z), z), Quaternion.Euler(0f, yaw, 0f));
            go.AddComponent<ObjectSpawnPoint>().objectId = objectId;
        }

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
                    // Only the creatures of the marsh stand in water.
                    if (HeightAt(p.x, p.y) < WaterLevel - 0.3f && creature != MireFrog && creature != MireSnapper && creature != Mireling) continue;
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
            // The boss of the Cinder Cut, facing the way in from the quarry.
            Spawn(RuukharTheSlagborn, CinderCut.x + 2f, CinderCut.y + 5f, 225f, 2f);
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
            // A warm sun and cool shade; a light blue haze that leaves the far mountains their shape.
            sun.transform.rotation = SunRotation;
            sun.color = new Color(1f, 0.92f, 0.8f);
            sun.intensity = 1.5f;
            sun.shadows = LightShadows.Soft;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.66f, 0.76f, 0.86f);
            RenderSettings.fogStartDistance = 170f;
            RenderSettings.fogEndDistance = 760f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.64f, 0.8f);
            RenderSettings.ambientEquatorColor = new Color(0.48f, 0.52f, 0.47f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.25f, 0.2f);
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
                new MapArea { name = "Mirewatch", center = Village, radius = VillageRadius + 8f, music = "music_mirewatch", ambience = "amb_village", rest = true },
                new MapArea { name = "Westbrook", center = Farm, radius = 110f },
                new MapArea { name = "The Old Mill", center = OldMill, radius = 30f },
                new MapArea { name = "Reedfen", center = Reedfen, radius = 160f, ambience = "amb_marsh" },
                new MapArea { name = "Rennick's Watch", center = RennickTower, radius = 25f, ambience = "amb_forest" },
                new MapArea { name = "The Greywood", center = Greywood, radius = 240f, ambience = "amb_forest" },
                new MapArea { name = "Ironvein Camp", center = BrannocCamp, radius = 25f },
                new MapArea { name = "The Cinder Cut", center = CinderCut, radius = 40f, music = "music_drowned", ambience = "amb_ruins" },
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
