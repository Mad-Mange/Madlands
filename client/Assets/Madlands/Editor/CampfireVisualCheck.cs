using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Madlands.Client.Editor
{
    /// <summary>Checks the actual world's fires and renders a close view with their embers.</summary>
    public static class CampfireVisualCheck
    {
        public static void Run()
        {
            try
            {
                var scene = EditorSceneManager.OpenScene(HollowmereBuilder.ScenePath);
                int count = CampfireAmbience.AttachToScene(scene);
                if (count != 6 || CampfireAmbience.AttachToScene(scene) != count)
                    throw new InvalidOperationException("Expected six campfires, with no duplicate installations: " + count);
                var fires = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CampfireAmbience>(true)).ToArray();
                if (fires.Length != count) throw new InvalidOperationException("Duplicate or missing campfire effects");
                foreach (var fire in fires)
                {
                    if (fire.GetComponentsInChildren<Collider>(true).Length != 0) throw new InvalidOperationException("Campfire became blocking");
                    foreach (string name in new[] { "FlameOuter", "FlameCore" })
                    {
                        var part = fire.transform.Find(name);
                        if (part == null || part.GetComponentInChildren<MeshRenderer>() == null) throw new InvalidOperationException("Missing flame " + name);
                        var materials = part.GetComponentInChildren<MeshRenderer>().sharedMaterials;
                        if (!materials.Any(m => m != null && m.name.Contains("Glow") && m.IsKeywordEnabled("_EMISSION")))
                            throw new InvalidOperationException(name + " has no emissive flame material: " + string.Join(", ", materials.Select(m => m != null ? m.name : "null")));
                        if ((GameObjectUtility.GetStaticEditorFlags(part.gameObject) & StaticEditorFlags.BatchingStatic) != 0)
                            throw new InvalidOperationException("Animated flame is statically batched: " + name);
                    }
                }

                var watch = GameObject.Find("RennicksWatch");
                var selected = watch != null ? watch.GetComponentInChildren<CampfireAmbience>() : fires[0];
                if (selected == null) throw new InvalidOperationException("The watch's campfire was not found");
                // MonoBehaviour Awake/Update do not run in edit mode: initialize and sample the same runtime effects explicitly.
                const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                typeof(CampfireAmbience).GetMethod("Awake", flags).Invoke(selected, null);
                var animate = typeof(CampfireAmbience).GetMethod("AnimateFlames", flags);
                animate.Invoke(selected, new object[] { 0f });
                var outer = selected.transform.Find("FlameOuter");
                var first = outer.localScale;
                animate.Invoke(selected, new object[] { 0.37f });
                if ((outer.localScale - first).sqrMagnitude < 1e-6f) throw new InvalidOperationException("Flames did not move");
                var particles = selected.GetComponentInChildren<ParticleSystem>();
                if (particles == null) throw new InvalidOperationException("No embers created");
                particles.Simulate(1.5f, true, true, true);
                if (particles.particleCount == 0) throw new InvalidOperationException("No embers emitted");

                var pos = selected.transform.position;
                var cameraObject = new GameObject("CampfirePreviewCamera");
                var camera = cameraObject.AddComponent<Camera>();
                camera.fieldOfView = 42f;
                camera.farClipPlane = 300f;
                var eye = pos + new Vector3(2.8f, 2.1f, -4.5f);
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(pos + Vector3.up * 0.5f - eye));
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
                string path = Path.Combine(MapExporter.RepoRoot, "builds", "campfire-world-preview.png");
                File.WriteAllBytes(path, image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
                Debug.Log("[CampfireVisualCheck] Six fires, moving unbatched flames and embers passed. Preview: " + path);
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
