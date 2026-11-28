using System;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Madlands.Client.Editor
{
    /// <summary>Checks the imported mesh orientation used by terrain-instanced ground cover.</summary>
    public static class GroundCoverCheck
    {
        public static void Run()
        {
            try
            {
                EditorSceneManager.OpenScene(HollowmereBuilder.ScenePath);
                var terrain = Terrain.activeTerrain;
                if (terrain == null) throw new InvalidOperationException("World terrain is missing");
                var prototypes = terrain.terrainData.detailPrototypes;
                if (prototypes.Length != 8) throw new InvalidOperationException("Ground cover prototypes are missing");
                foreach (var detail in prototypes)
                {
                    var root = detail.prototype;
                    if (root == null || !detail.useInstancing || !detail.usePrototypeMesh)
                        throw new InvalidOperationException("Invalid ground cover prototype");
                    if (root.name != "Pebbles" && detail.alignToGround > 0.001f)
                        throw new InvalidOperationException(root.name + " leans with the terrain");
                    var filter = root.GetComponent<MeshFilter>();
                    var renderer = root.GetComponent<MeshRenderer>();
                    if (filter == null || filter.sharedMesh == null || renderer == null || renderer.sharedMaterials.Length != 1)
                        throw new InvalidOperationException(root.name + " must have one root mesh and material");
                    if (Quaternion.Angle(root.transform.localRotation, Quaternion.identity) > 0.1f)
                        throw new InvalidOperationException(root.name + " has an unbaked root rotation");
                    var bounds = filter.sharedMesh.bounds;
                    if (bounds.min.y < -0.1f || bounds.max.y < 0.03f)
                        throw new InvalidOperationException(root.name + " is not positioned above its root: " + bounds);
                    if (root.name == "GrassTall" && bounds.size.y < 0.7f)
                        throw new InvalidOperationException("Tall grass is lying on its side: " + bounds);
                    if (root.name == "Pebbles" && bounds.size.y > bounds.size.z)
                        throw new InvalidOperationException("Pebbles are standing on their side: " + bounds);
                    using (var meshData = MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh))
                    using (var colors = new NativeArray<Color32>(meshData[0].vertexCount, Allocator.Temp))
                    {
                        if (!meshData[0].HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color))
                            throw new InvalidOperationException(root.name + " has no vertex colors");
                        meshData[0].GetColors(colors);
                        byte expectedWind = root.name == "Pebbles" ? (byte)0 : (byte)255;
                        for (int i = 0; i < colors.Length; i++)
                            if (colors[i].a != expectedWind) throw new InvalidOperationException(root.name + " has an incorrect wind weight");
                    }
                }
                // Inspect actual generated instances, including a sloped forest patch and the marsh.
                var data = terrain.terrainData;
                int checkedInstances = 0;
                foreach (var spot in new[] { new Vector2(235f, -95f), new Vector2(-285f, 235f), new Vector2(0f, 320f) })
                {
                    var origin = terrain.GetPosition();
                    int x = Mathf.Clamp(Mathf.FloorToInt((spot.x - origin.x) / data.size.x * data.detailPatchCount), 0, data.detailPatchCount - 1);
                    int z = Mathf.Clamp(Mathf.FloorToInt((spot.y - origin.z) / data.size.z * data.detailPatchCount), 0, data.detailPatchCount - 1);
                    for (int layer = 0; layer < prototypes.Length; layer++)
                    {
                        var instances = data.ComputeDetailInstanceTransforms(x, z, layer, terrain.detailObjectDensity, out _);
                        foreach (var instance in instances)
                        {
                            float expectedHeight = data.GetInterpolatedHeight(instance.posX / data.size.x, instance.posZ / data.size.z);
                            if (Mathf.Abs(instance.posY - expectedHeight) > 0.05f)
                                throw new InvalidOperationException("Ground cover is not rooted at the terrain height");
                            checkedInstances++;
                        }
                    }
                }
                if (checkedInstances == 0) throw new InvalidOperationException("No terrain detail instances were checked");
                Debug.Log($"[GroundCoverCheck] Eight prototypes and {checkedInstances} terrain instances passed orientation, root, wind and material checks.");
                EditorApplication.Exit(0);
            }
            catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }
    }
}
