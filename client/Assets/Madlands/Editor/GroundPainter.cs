using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Madlands.Client.Editor
{
    /// <summary>
    /// Painted ground for terrain layers, made from code like the rest of the map: a canvas that tiles
    /// seamlessly, drawn on with tones, strokes (blades of grass, twigs), pebbles, leaves and rock facets.
    /// Everything drawn also raises a height field, which becomes the layer's normal map, so the ground
    /// catches the light in the shape of what is painted on it.
    /// </summary>
    public static class GroundPainter
    {
        public const int N = 512;

        public sealed class Canvas
        {
            public readonly Color[] Colors = new Color[N * N];
            public readonly float[] Heights = new float[N * N];
            public readonly System.Random Rng;

            public Canvas(int seed)
            {
                Rng = new System.Random(seed);
            }

            public float Next(float min, float max) => min + (float)Rng.NextDouble() * (max - min);

            private static int Index(int x, int y) => ((y % N + N) % N) * N + (x % N + N) % N;

            /// <summary>Tones between a and b in blotches of two sizes and a little grain; raises the height field by the tone.</summary>
            public void Tones(Color a, Color b, int coarse, int fine, float grain, float height = 0f)
            {
                float[] big = Grid(coarse), small = Grid(fine);
                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        float u = (x + 0.5f) / N, v = (y + 0.5f) / N;
                        float t = 0.62f * Periodic(big, coarse, u, v) + 0.38f * Periodic(small, fine, u, v) + grain * ((float)Rng.NextDouble() - 0.5f);
                        Colors[y * N + x] = Color.Lerp(a, b, t);
                        Heights[y * N + x] = t * height;
                    }
                }
            }

            /// <summary>
            /// Blotches of <paramref name="color"/> where a noise of the given size (and a finer one breaking up its
            /// outline) is above <paramref name="threshold"/> (0..1).
            /// </summary>
            public void Patches(Color color, int size, float threshold, float softness, float height = 0f)
            {
                int fine = size * 3 + 1;
                float[] grid = Grid(size), detail = Grid(fine);
                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        float u = (x + 0.5f) / N, v = (y + 0.5f) / N;
                        float noise = 0.7f * Periodic(grid, size, u, v) + 0.3f * Periodic(detail, fine, u, v);
                        float a = Mathf.Clamp01((noise - threshold) / softness);
                        if (a <= 0f) continue;
                        int i = y * N + x;
                        Colors[i] = Color.Lerp(Colors[i], color, a);
                        Heights[i] += a * height;
                    }
                }
            }

            /// <summary>A round dab, soft at its rim, blended over the canvas.</summary>
            public void Dab(float cx, float cy, float radius, Color color, float height, float alpha = 1f)
            {
                int r = Mathf.CeilToInt(radius + 1f);
                for (int y = Mathf.FloorToInt(cy) - r; y <= Mathf.CeilToInt(cy) + r; y++)
                {
                    for (int x = Mathf.FloorToInt(cx) - r; x <= Mathf.CeilToInt(cx) + r; x++)
                    {
                        float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                        float cover = Mathf.Clamp01(radius + 0.5f - d) * alpha;
                        if (cover <= 0f) continue;
                        int i = Index(x, y);
                        Colors[i] = Color.Lerp(Colors[i], color, cover);
                        Heights[i] = Mathf.Lerp(Heights[i], Mathf.Max(Heights[i], height), cover);
                    }
                }
            }

            /// <summary>A stroke tapering from its root to its tip (a blade of grass, a twig), shading from one color to the other.</summary>
            public void Stroke(float x, float y, float angle, float length, float width, Color root, Color tip, float height)
            {
                float dx = Mathf.Cos(angle), dy = Mathf.Sin(angle);
                int steps = Mathf.Max(2, Mathf.CeilToInt(length * 1.5f));
                for (int s = 0; s <= steps; s++)
                {
                    float f = (float)s / steps;
                    Dab(x + dx * length * f, y + dy * length * f, width * (1f - 0.75f * f) * 0.5f, Color.Lerp(root, tip, f), height * (0.4f + 0.6f * f));
                }
            }

            /// <summary>
            /// An ellipse lit from the lower right (the sun is in the south-east) and shaded toward the upper left, with a soft shadow beside it
            /// (a pebble, a fallen leaf), domed up in the height field.
            /// </summary>
            public void Pebble(float cx, float cy, float rx, float ry, float angle, Color color, float height, float shadow = 0.35f)
            {
                float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
                int r = Mathf.CeilToInt(Mathf.Max(rx, ry) + 2f);
                if (shadow > 0f) Dab(cx - 0.8f, cy + 0.8f, Mathf.Max(rx, ry) * 0.9f, Color.Lerp(color, Color.black, 0.6f), 0f, shadow);
                for (int y = Mathf.FloorToInt(cy) - r; y <= Mathf.CeilToInt(cy) + r; y++)
                {
                    for (int x = Mathf.FloorToInt(cx) - r; x <= Mathf.CeilToInt(cx) + r; x++)
                    {
                        float px = x + 0.5f - cx, py = y + 0.5f - cy;
                        float lx = (px * c + py * s) / rx, ly = (-px * s + py * c) / ry;
                        float e = lx * lx + ly * ly;
                        float cover = Mathf.Clamp01((1f - e) * Mathf.Min(rx, ry) * 0.5f + 0.5f);
                        if (cover <= 0f) continue;
                        // Light from the lower right of the canvas (+u east, +v north).
                        float shade = 1f + 0.22f * ((px / rx) - (py / ry)) * 0.7f - 0.12f * e;
                        int i = Index(x, y);
                        Colors[i] = Color.Lerp(Colors[i], color * shade, cover);
                        Heights[i] = Mathf.Lerp(Heights[i], height * Mathf.Sqrt(Mathf.Max(0f, 1f - e)), cover);
                    }
                }
            }

            /// <summary>
            /// Rock: flat facets (cells around random points) each in its own tone between a and b and tilted its own
            /// way in the height field, edged by dark cracks.
            /// </summary>
            public void Facets(int cells, Color a, Color b, Color crack, float height)
            {
                var points = new Vector2[cells];
                var tones = new float[cells];
                var tilts = new Vector2[cells];
                for (int k = 0; k < cells; k++)
                {
                    points[k] = new Vector2(Next(0f, N), Next(0f, N));
                    tones[k] = Next(0f, 1f);
                    tilts[k] = new Vector2(Next(-1f, 1f), Next(-1f, 1f));
                }
                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        float d1 = float.MaxValue, d2 = float.MaxValue;
                        int nearest = 0;
                        Vector2 offset = Vector2.zero;
                        for (int k = 0; k < cells; k++)
                        {
                            // The nearest copy of the point on the wrapped canvas.
                            var q = points[k] - p;
                            q.x -= Mathf.Round(q.x / N) * N;
                            q.y -= Mathf.Round(q.y / N) * N;
                            float d = q.magnitude;
                            if (d < d1)
                            {
                                d2 = d1;
                                d1 = d;
                                nearest = k;
                                offset = q;
                            }
                            else if (d < d2) d2 = d;
                        }
                        int i = y * N + x;
                        float lit = Mathf.Clamp(-Vector2.Dot(offset, tilts[nearest]) / 60f, -0.25f, 0.25f);
                        var tone = Color.Lerp(a, b, Mathf.Clamp01(tones[nearest] + lit));
                        float edge = Mathf.Clamp01((d2 - d1 - 0.6f) / 1.8f);
                        Colors[i] = Color.Lerp(Color.Lerp(tone, crack, 0.65f), Color.Lerp(Colors[i], tone, 0.7f), edge);
                        Heights[i] = height * (0.5f + 0.5f * tones[nearest] + Vector2.Dot(offset, tilts[nearest]) / 120f) * edge;
                    }
                }
            }

            private float[] Grid(int size)
            {
                var grid = new float[size * size];
                for (int i = 0; i < grid.Length; i++) grid[i] = (float)Rng.NextDouble();
                return grid;
            }
        }

        /// <summary>Value noise that wraps at the canvas edges.</summary>
        private static float Periodic(float[] grid, int size, float u, float v)
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

        /// <summary>
        /// Paints a canvas, saves it as <c>dir/Textures/name.png</c> with its normal map, and makes the terrain layer
        /// (tile size in meters, smoothness, how strongly the normal map bends the light).
        /// </summary>
        public static TerrainLayer Layer(string dir, string name, int seed, float tileSize, float smoothness, float normalScale, Action<Canvas> paint)
        {
            var canvas = new Canvas(seed);
            paint(canvas);
            string texPath = $"{dir}/Textures/{name}.png";
            string normalPath = $"{dir}/Textures/{name}_Normal.png";
            Save(texPath, canvas.Colors);
            Save(normalPath, Normals(canvas.Heights));
            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(normalPath, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(normalPath) is TextureImporter importer && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }

            var layer = new TerrainLayer
            {
                diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath),
                normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath),
                normalScale = normalScale,
                tileSize = new Vector2(tileSize, tileSize),
                smoothness = smoothness,
            };
            string layerPath = $"{dir}/{name}.terrainlayer";
            AssetDatabase.DeleteAsset(layerPath);
            AssetDatabase.CreateAsset(layer, layerPath);
            return layer;
        }

        private static void Save(string assetPath, Color[] colors)
        {
            var tex = new Texture2D(N, N, TextureFormat.RGB24, false);
            tex.SetPixels(colors);
            File.WriteAllBytes(Path.Combine(Application.dataPath, "..", assetPath), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        /// <summary>A tangent-space normal map (OpenGL convention, +v up) from a height field in pixels.</summary>
        private static Color[] Normals(float[] heights)
        {
            var colors = new Color[N * N];
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float du = heights[y * N + (x + 1) % N] - heights[y * N + (x + N - 1) % N];
                    float dv = heights[(y + 1) % N * N + x] - heights[(y + N - 1) % N * N + x];
                    var n = new Vector3(-du * 0.5f, -dv * 0.5f, 1f).normalized;
                    colors[y * N + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f);
                }
            }
            return colors;
        }

        // ------------------------------------------------------------------ recipes

        private static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        /// <summary>Grass: blotchy tones under thousands of short blades, dark at the root and catching the light at the tip.</summary>
        public static void Grass(Canvas c, string dark, string light, string bladeRoot, string bladeTip, string accent, int blades)
        {
            c.Tones(Hex(dark), Hex(light), 5, 22, 0.12f, 0.6f);
            Color root = Hex(bladeRoot), tip = Hex(bladeTip), extra = Hex(accent);
            for (int k = 0; k < blades; k++)
            {
                float x = c.Next(0f, N), y = c.Next(0f, N);
                float angle = c.Next(0f, Mathf.PI * 2f);
                bool odd = c.Rng.NextDouble() < 0.12;
                c.Stroke(x, y, angle, c.Next(5f, 11f), c.Next(1.6f, 2.6f), Color.Lerp(root, Hex(dark), c.Next(0f, 0.5f)), odd ? extra : Color.Lerp(tip, Hex(light), c.Next(0f, 0.6f)), c.Next(1.5f, 3f));
            }
        }

        /// <summary>Bare earth: brown tones, darker damp patches, pebbles and specks.</summary>
        public static void Dirt(Canvas c, string dark, string light, string damp, string pebble, int pebbles)
        {
            c.Tones(Hex(dark), Hex(light), 6, 28, 0.16f, 0.8f);
            c.Patches(Hex(damp), 9, 0.55f, 0.35f, -0.3f);
            Color stone = Hex(pebble);
            for (int k = 0; k < pebbles; k++)
            {
                float r = c.Next(1.5f, 5.5f);
                c.Pebble(c.Next(0f, N), c.Next(0f, N), r, r * c.Next(0.6f, 0.9f), c.Next(0f, Mathf.PI), Color.Lerp(stone, Hex(dark), c.Next(0f, 0.5f)), r * 0.9f);
            }
            for (int k = 0; k < pebbles * 3; k++)
                c.Dab(c.Next(0f, N), c.Next(0f, N), c.Next(0.5f, 1.2f), Hex(dark) * 0.75f, 0f, 0.7f);
        }

        /// <summary>Rock: facets in tones of grey, cracked, weathered with a little moss.</summary>
        public static void Rock(Canvas c, string dark, string light, string crack, string moss)
        {
            c.Tones(Hex(dark), Hex(light), 4, 16, 0.08f);
            c.Facets(24, Hex(dark), Hex(light), Hex(crack), 6f);
            c.Patches(Hex(moss), 6, 0.66f, 0.12f, 0.5f);
        }

        /// <summary>Forest floor: dark earth under fallen leaves, moss and twigs.</summary>
        public static void Forest(Canvas c, string dark, string light, string moss, string[] leaves, int count)
        {
            c.Tones(Hex(dark), Hex(light), 6, 24, 0.14f, 0.6f);
            c.Patches(Hex(moss), 8, 0.55f, 0.18f, 0.8f);
            for (int k = 0; k < count; k++)
            {
                float r = c.Next(2.5f, 5f);
                var color = Hex(leaves[c.Rng.Next(leaves.Length)]);
                c.Pebble(c.Next(0f, N), c.Next(0f, N), r, r * c.Next(0.4f, 0.6f), c.Next(0f, Mathf.PI), Color.Lerp(color, Hex(dark), c.Next(0f, 0.45f)), 1.2f, 0.25f);
            }
            for (int k = 0; k < count / 6; k++)
                c.Stroke(c.Next(0f, N), c.Next(0f, N), c.Next(0f, Mathf.PI * 2f), c.Next(8f, 20f), 1.4f, Hex(dark) * 0.7f, Hex(light), 1.5f);
        }

        /// <summary>Mud: dark wet earth, glossy pools, a few stones.</summary>
        public static void Mud(Canvas c, string dark, string light, string wet, string pebble)
        {
            c.Tones(Hex(dark), Hex(light), 5, 26, 0.1f, 0.6f);
            c.Patches(Hex(wet), 5, 0.55f, 0.25f, -0.3f);
            for (int k = 0; k < 60; k++)
            {
                float r = c.Next(1.5f, 4f);
                c.Pebble(c.Next(0f, N), c.Next(0f, N), r, r * 0.75f, c.Next(0f, Mathf.PI), Hex(pebble), r * 0.6f, 0.2f);
            }
        }

        /// <summary>A shingle beach: pale grit full of small rounded stones.</summary>
        public static void Shingle(Canvas c, string dark, string light, string[] stones, int count)
        {
            c.Tones(Hex(dark), Hex(light), 6, 40, 0.22f, 0.5f);
            for (int k = 0; k < count; k++)
            {
                float r = c.Next(1.2f, 4.5f);
                var color = Hex(stones[c.Rng.Next(stones.Length)]);
                c.Pebble(c.Next(0f, N), c.Next(0f, N), r, r * c.Next(0.65f, 0.95f), c.Next(0f, Mathf.PI), Color.Lerp(color, Hex(light), c.Next(0f, 0.3f)), r * 0.8f, 0.25f);
            }
        }

        /// <summary>Snow: soft white drifts with blue shade between them.</summary>
        public static void Snow(Canvas c, string shade, string light)
        {
            c.Tones(Hex(shade), Hex(light), 4, 14, 0.04f, 3f);
            c.Patches(Hex(light), 6, 0.5f, 0.3f, 1f);
        }
    }
}
