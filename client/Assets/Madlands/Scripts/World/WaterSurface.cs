using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Madlands.Client
{
    /// <summary>
    /// A map's water: one mesh at this object's height over every place where the terrain dips under it (a mere,
    /// marsh pools, a lagoon), built when the map loads from the terrain's own height samples, so the shore follows
    /// the ground exactly and no mesh is stored. Its vertex colors hold the depth there for the Madlands/Water
    /// shader (red = (depth + 0.5) / 3 meters): light over the shallows, dark where it is deep, foam at the shore.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class WaterSurface : MonoBehaviour
    {
        // Start, not Awake: the terrains of the scene are enabled by then.
        private void Start() => Build();

        /// <summary>Builds the mesh over the terrains of its scene (map builders call it to see the water in the editor).</summary>
        public void Build()
        {
            float level = transform.position.y;
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            foreach (var terrain in Terrain.activeTerrains)
                if (terrain.gameObject.scene == gameObject.scene)
                    AddTerrain(terrain, level, vertices, colors, triangles);
            var mesh = new Mesh { name = "Water", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        private void AddTerrain(Terrain terrain, float level, List<Vector3> vertices, List<Color> colors, List<int> triangles)
        {
            var data = terrain.terrainData;
            int n = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, n, n);
            var origin = terrain.GetPosition() - transform.position;
            float stepX = data.size.x / (n - 1), stepZ = data.size.z / (n - 1);
            float Depth(int x, int z) => level - (heights[z, x] * data.size.y + terrain.GetPosition().y);
            var index = new Dictionary<int, int>();
            int Vertex(int x, int z)
            {
                if (index.TryGetValue(z * n + x, out int i)) return i;
                index[z * n + x] = vertices.Count;
                vertices.Add(new Vector3(origin.x + x * stepX, 0f, origin.z + z * stepZ));
                colors.Add(new Color(Mathf.Clamp01((Depth(x, z) + 0.5f) / 3f), 0f, 0f, 1f));
                return vertices.Count - 1;
            }
            for (int z = 0; z + 1 < n; z++)
            {
                for (int x = 0; x + 1 < n; x++)
                {
                    // Cells where all four corners stand clear of the water have none.
                    if (Mathf.Max(Mathf.Max(Depth(x, z), Depth(x + 1, z)), Mathf.Max(Depth(x, z + 1), Depth(x + 1, z + 1))) < -0.05f) continue;
                    int a = Vertex(x, z), b = Vertex(x + 1, z), c = Vertex(x + 1, z + 1), d = Vertex(x, z + 1);
                    triangles.Add(a);
                    triangles.Add(d);
                    triangles.Add(c);
                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(b);
                }
            }
        }
    }
}
