using System;
using System.IO;
using System.Linq;
using Madlands.Shared.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Madlands.Client.Editor
{
    /// <summary>Batch-mode smoke checks and a terrain-rendered preview of the Cinder Quake warning.</summary>
    public static class ImprovementVisualCheck
    {
        public static void Run()
        {
            try
            {
                EditorSceneManager.OpenScene(HollowmereBuilder.ScenePath);
                var catalog = ArtCatalog.Instance;
                if (catalog == null) throw new InvalidOperationException("Art catalog is missing");
                foreach (uint id in new uint[] { 1, 100, 101, 102, 103, 104, 200, 201, 202, 203, 204, 205, 300, 301, 302, 303, 304, 305 })
                    if (catalog.GetIcon("SpellIcon" + id) == null) throw new InvalidOperationException("Missing spell icon " + id);
                foreach (uint id in new uint[] { 1101, 1102, 1103, 1301, 1302, 1303, 5001, 5002, 5003, 5004 })
                    if (catalog.GetIcon("ItemIcon" + id) == null) throw new InvalidOperationException("Missing item icon " + id);
                foreach (uint id in new uint[] { 500, 501, 502, 503, 504, 505, 506, 507, 508, 900, 901, 902, 903, 904, 905, 906, 908, 909, 910, 911, 912, 913, 914, 915 })
                {
                    var effectIcon = SpellIcons.Create(new SpellInfo { Icon = id, Name = "Effect" });
                    if (effectIcon.style.backgroundImage.value.texture == null) throw new InvalidOperationException("Missing shared effect icon " + id);
                }

                // One shared action-bar element changes between a pictured item, a pictured spell and a fallback spell.
                var icon = SpellIcons.Create(new SpellInfo { Icon = 100, Name = "Heroic Strike" });
                if (icon.style.backgroundImage.value.texture != catalog.GetIcon("SpellIcon100")) throw new InvalidOperationException("Spell picture not applied");
                ItemIcons.Apply(icon, new ItemInfo { Icon = 1101, Name = "Recruit's Tunic" });
                if (icon.style.backgroundImage.value.texture != catalog.GetIcon("ItemIcon1101")) throw new InvalidOperationException("Item picture not applied");
                SpellIcons.Apply(icon, new SpellInfo { Icon = 200, Name = "Shard Bolt" });
                if (icon.style.backgroundImage.value.texture != catalog.GetIcon("SpellIcon200")) throw new InvalidOperationException("Spell picture did not replace the item picture");
                SpellIcons.Apply(icon, new SpellInfo { Icon = 500, Name = "Food" });
                if (icon.style.backgroundImage.value.texture != catalog.GetIcon("ItemIcon4001")) throw new InvalidOperationException("Food did not reuse its item picture");
                SpellIcons.Apply(icon, new SpellInfo { Icon = 999999, Name = "Unknown Spell" });
                if (icon.style.backgroundImage.value.texture != null || icon.Q<Label>().text != "US") throw new InvalidOperationException("Spell fallback retained an old picture");

                var markers = GameObject.Find("RoadMarkers");
                if (markers == null || markers.transform.childCount != 8 || markers.GetComponentsInChildren<Collider>().Length != 0)
                    throw new InvalidOperationException("Road markers missing or blocking");

                var terrain = Terrain.activeTerrain;
                if (terrain == null) throw new InvalidOperationException("Terrain missing");
                Physics.SyncTransforms();
                foreach (Transform marker in markers.transform)
                {
                    var markerPos = marker.position;
                    if (terrain.SampleHeight(markerPos) + terrain.GetPosition().y <= HollowmereBuilder.WaterLevel)
                        Debug.LogWarning("[ImprovementVisualCheck] Road marker is at or below water: " + markerPos);
                    var obstacles = Physics.OverlapCapsule(markerPos + Vector3.up * 0.3f, markerPos + Vector3.up * 1.3f, 0.3f, ~0, QueryTriggerInteraction.Ignore);
                    foreach (var obstacle in obstacles)
                        if (!(obstacle is TerrainCollider)) Debug.LogWarning("[ImprovementVisualCheck] Road marker overlaps " + obstacle.name + " at " + markerPos);
                }
                var pos = new Vector3(HollowmereBuilder.CinderCut.x, 0f, HollowmereBuilder.CinderCut.y);
                pos.y = terrain.SampleHeight(pos) + terrain.GetPosition().y;
                var owner = new GameObject("WarningPreviewOwner");
                owner.transform.position = pos;
                var warning = DangerTelegraph.Create(owner.transform);
                warning.SetProgress(0.75f);
                var meshes = warning.GetComponentsInChildren<MeshFilter>().Select(f => f.sharedMesh).ToArray();
                if (meshes.Length != 2 || meshes.Any(m => m == null || m.vertexCount == 0)) throw new InvalidOperationException("Warning meshes missing");
                if (Mathf.Abs(meshes[0].bounds.extents.x - TelegraphHints.CinderQuakeRadius) > 0.05f)
                    throw new InvalidOperationException("Warning radius is incorrect");

                var cameraObject = new GameObject("WarningPreviewCamera");
                var cam = cameraObject.AddComponent<Camera>();
                cam.fieldOfView = 55f;
                cam.farClipPlane = 500f;
                cam.transform.SetPositionAndRotation(pos + new Vector3(0f, 18f, -25f), Quaternion.LookRotation(pos - (pos + new Vector3(0f, 18f, -25f))));
                var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                void Capture(string filename)
                {
                    cam.Render();
                    cam.Render();
                    var previous = RenderTexture.active;
                    RenderTexture.active = rt;
                    var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    image.Apply();
                    RenderTexture.active = previous;
                    var path = Path.Combine(MapExporter.RepoRoot, "builds", filename);
                    File.WriteAllBytes(path, image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);
                    Debug.Log("[ImprovementVisualCheck] Preview: " + path);
                }
                Capture("cinder-warning-preview.png");
                var firstMarkerPosition = markers.transform.GetChild(0).position;
                var eye = firstMarkerPosition + new Vector3(3f, 2.4f, -5f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(firstMarkerPosition + Vector3.up - eye));
                Capture("waystone-world-preview.png");
                Debug.Log("[ImprovementVisualCheck] Icons, fallback, landmarks and warning mesh passed");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }
    }
}
