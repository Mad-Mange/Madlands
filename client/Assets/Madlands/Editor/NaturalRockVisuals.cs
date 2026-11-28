using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Madlands.Client.Editor
{
    /// <summary>Replaces only the visible rock meshes, retaining the collision exported to the server.</summary>
    public static class NaturalRockVisuals
    {
        private static readonly string[] Models = { "Cliff1", "Cliff2", "Outcrop1", "Outcrop2", "RockSpire" };
        private const string VisualName = "NaturalRockVisual";

        public static bool Install(GameObject rock, string model)
        {
            if (!Models.Contains(model)) throw new ArgumentException("Unknown formation: " + model);
            if (rock.transform.Find(VisualName) != null) return false;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Madlands/Art/Props/{model}Natural.fbx");
            if (prefab == null) throw new InvalidOperationException("Missing natural rock model: " + model);
            var originals = rock.GetComponentsInChildren<Renderer>(true);
            if (originals.Length == 0 || rock.GetComponentsInChildren<MeshCollider>(true).Length == 0)
                throw new InvalidOperationException("Formation has no original renderer or collision: " + model);
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            visual.name = VisualName;
            visual.transform.SetParent(rock.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            if (visual.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new InvalidOperationException("Natural visual unexpectedly contains collision: " + model);
            foreach (var renderer in originals)
            {
                renderer.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            foreach (var t in visual.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
            return true;
        }

        // One-time migration for the checked-in scene. Building the world installs these directly.
        public static void InstallExistingFromCommandLine()
        {
            try
            {
                foreach (var model in Models)
                    if (AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Madlands/Art/Props/{model}Natural.fbx") == null)
                        throw new InvalidOperationException("Missing natural rock model: " + model);
                var scene = EditorSceneManager.OpenScene(HollowmereBuilder.ScenePath);
                var parent = GameObject.Find("RockFormations");
                if (parent == null) throw new InvalidOperationException("RockFormations not found");
                var rocks = Enumerable.Range(0, parent.transform.childCount).Select(i => parent.transform.GetChild(i).gameObject).ToArray();
                if (rocks.Length == 0 || rocks.Any(r => !Models.Contains(r.name)))
                    throw new InvalidOperationException("Unexpected rock formation group");

                var before = CollisionExporter.Build(out int beforeCount);
                string beforePath = Path.Combine(MapExporter.RepoRoot, "builds", "rocks-collision-before.bin");
                string afterPath = Path.Combine(MapExporter.RepoRoot, "builds", "rocks-collision-after.bin");
                before.Save(beforePath);
                var selected = rocks.Where(r => r.name.StartsWith("Outcrop")).OrderBy(r => (r.transform.position - new Vector3(0, 0, -148)).sqrMagnitude).First();
                var bounds = selected.GetComponentInChildren<Renderer>().bounds;
                var eye = bounds.center + new Vector3(1f, .65f, -1f).normalized * Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) * 1.7f;
                Preview(bounds.center, eye, "rocks-world-before.png");
                int installed = 0;
                foreach (var rock in rocks) if (Install(rock, rock.name)) installed++;
                var newBounds = selected.transform.Find(VisualName).GetComponentInChildren<Renderer>().bounds;
                Debug.Log($"[NaturalRockVisuals] Preview {selected.name}: old bounds {bounds}, new bounds {newBounds}");
                if (Vector3.Distance(bounds.center, newBounds.center) > bounds.size.magnitude * .5f || newBounds.size.magnitude < bounds.size.magnitude * .5f || newBounds.size.magnitude > bounds.size.magnitude * 1.6f)
                    throw new InvalidOperationException("Natural rock is misaligned with its original");
                Preview(bounds.center, eye, "rocks-world-after.png");
                var after = CollisionExporter.Build(out int afterCount);
                after.Save(afterPath);
                using (var sha = SHA256.Create())
                using (var a = File.OpenRead(beforePath))
                using (var b = File.OpenRead(afterPath))
                    if (beforeCount != afterCount || !sha.ComputeHash(a).SequenceEqual(sha.ComputeHash(b)))
                        throw new InvalidOperationException("The exported collision changed");
                var pebbles = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Madlands/Art/Ground/Pebbles.fbx");
                if (pebbles == null || pebbles.GetComponentInChildren<MeshRenderer>()?.sharedMaterials.Length != 1)
                    throw new InvalidOperationException("Pebbles must have one material for terrain details");
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save world scene");
                Debug.Log($"[NaturalRockVisuals] Installed {installed} of {rocks.Length} formations; {beforeCount} colliders unchanged byte-for-byte; pebbles have one material.");
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        private static void Preview(Vector3 target, Vector3 eye, string name)
        {
            var cameraObject = new GameObject("RockPreviewCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 42f;
            camera.farClipPlane = 500f;
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = rt;
            camera.Render();
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(MapExporter.RepoRoot, "builds", name), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
