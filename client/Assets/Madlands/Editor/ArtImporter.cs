using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Madlands.Client.Editor
{
    /// <summary>
    /// Import rules for the Blender art in Assets/Madlands/Art (art/README.md), so a new FBX needs no
    /// clicking in the inspector: Characters/ are humanoids (Unity maps the avatar from the bone names),
    /// Creatures/ have generic rigs, everything else is a static mesh. Materials become URP Lit with the
    /// Blender color. Keeps <see cref="ArtCatalog"/> (Resources/ArtCatalog.asset) in sync.
    /// </summary>
    public sealed class ArtImporter : AssetPostprocessor
    {
        public const string ArtRoot = "Assets/Madlands/Art/";
        public const string CatalogPath = "Assets/Madlands/Resources/ArtCatalog.asset";

        /// <summary>
        /// Animation-only files and the model they were animated on: they use that model's avatar, so
        /// Unity measures the clips against the real T-pose (the file's own pose is the first frame).
        /// </summary>
        public static readonly Dictionary<string, string> AnimationSkeletons = new Dictionary<string, string>
        {
            { "HumanoidAnimations", "BrightvaleHuman" },
        };

        /// <summary>Clips that repeat; everything else plays once.</summary>
        public static readonly HashSet<string> LoopingClips = new HashSet<string>
        {
            "Idle", "Walk", "Run", "RunBack", "StrafeLeft", "StrafeRight", "Fall", "CastLoop", "Sit", "Swim",
        };

        private bool IsArt => assetPath.StartsWith(ArtRoot, System.StringComparison.Ordinal);

        public override int GetPostprocessOrder() => 10; // after URP's own material conversion

        private void OnPreprocessModel()
        {
            if (!IsArt) return;
            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.importBlendShapes = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.optimizeGameObjects = false; // weapon grips and attachments hang on bone transforms

            bool character = assetPath.Contains("/Characters/");
            bool creature = assetPath.Contains("/Creatures/");
            if (character || creature)
            {
                importer.animationType = character ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                if (AnimationSkeletons.TryGetValue(Path.GetFileNameWithoutExtension(assetPath), out var skeleton))
                {
                    string skeletonPath = Path.GetDirectoryName(assetPath).Replace('\\', '/') + "/" + skeleton + ".fbx";
                    context.DependsOnArtifact(skeletonPath);
                    var avatar = AssetDatabase.LoadAllAssetsAtPath(skeletonPath).OfType<Avatar>().FirstOrDefault();
                    if (avatar != null)
                    {
                        importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                        importer.sourceAvatar = avatar;
                    }
                    else Debug.LogWarning($"[ArtImporter] {assetPath}: no avatar in {skeletonPath} yet");
                }
                importer.importAnimation = true;
                importer.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
            }
            else
            {
                importer.animationType = ModelImporterAnimationType.None;
                importer.importAnimation = false;
            }
        }

        /// <summary>
        /// Unity keeps Blender's orientation, so a model facing Blender's front (-Y) faces -Z. Turn the
        /// root around: every model faces +Z (Unity's forward) and +X is its right hand.
        /// </summary>
        private void OnPostprocessModel(GameObject root)
        {
            if (!IsArt) return;
            root.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * root.transform.localRotation;
        }

        private void OnPreprocessAnimation()
        {
            if (!IsArt) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                // Blender names takes "Armature|Run".
                int bar = clip.name.LastIndexOf('|');
                if (bar >= 0) clip.name = clip.name.Substring(bar + 1);
                clip.loopTime = LoopingClips.Contains(clip.name);
                // Animations are made in place; the server moves units. Keep the hips motion in the pose.
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }

        private void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
        {
            if (!IsArt) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return;
            material.shader = shader;
            // Blender writes its linear base color; Unity material colors are sRGB.
            var color = Color.gray;
            if (description.TryGetProperty("DiffuseColor", out Vector4 diffuse))
                color = new Color(diffuse.x, diffuse.y, diffuse.z, 1f).gamma;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", description.materialName.Contains("Metal") ? 0.45f : 0.12f);
            material.SetFloat("_Metallic", description.materialName.Contains("Metal") ? 0.6f : 0f);
            if (description.materialName.Contains("Glow"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 1.5f);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool IsArtPath(string p) => p.StartsWith(ArtRoot, System.StringComparison.Ordinal);
            if (imported.Any(IsArtPath) || deleted.Any(IsArtPath) || moved.Any(IsArtPath) || movedFrom.Any(IsArtPath))
                RefreshCatalog();
        }

        /// <summary>Rebuilds the catalog from the FBX files under <see cref="ArtRoot"/>.</summary>
        [MenuItem("Madlands/Refresh Art Catalog")]
        public static void RefreshCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ArtCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ArtCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.Models.Clear();
            catalog.Clips.Clear();
            var guids = AssetDatabase.FindAssets("t:Model", new[] { ArtRoot.TrimEnd('/') });
            foreach (var path in guids.Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(p => p, System.StringComparer.Ordinal))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null) catalog.Models.Add(new ArtCatalog.ModelEntry { Name = name, Prefab = prefab });
                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                {
                    if (clip.name.StartsWith("__preview__", System.StringComparison.Ordinal)) continue;
                    catalog.Clips.Add(new ArtCatalog.ClipEntry { Set = name, Name = clip.name, Clip = clip });
                }
            }
            catalog.Invalidate();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
            Debug.Log($"[ArtImporter] Art catalog: {catalog.Models.Count} models, {catalog.Clips.Count} clips");
        }

        /// <summary>
        /// Logs what Unity made of each model (size, avatar, materials, clips), for checking new art from
        /// the command line: -executeMethod Madlands.Client.Editor.ArtImporter.ReportFromCommandLine
        /// </summary>
        [MenuItem("Madlands/Art Report")]
        public static void Report()
        {
            RefreshCatalog();
            var catalog = AssetDatabase.LoadAssetAtPath<ArtCatalog>(CatalogPath);
            var text = new System.Text.StringBuilder("[ArtImporter] Report\n");
            foreach (var model in catalog.Models)
            {
                var root = model.Prefab.transform;
                var bounds = new Bounds(root.position, Vector3.zero);
                foreach (var r in model.Prefab.GetComponentsInChildren<Renderer>())
                    bounds.Encapsulate(r.bounds);
                var animator = model.Prefab.GetComponent<Animator>();
                var avatar = animator != null ? animator.avatar : null;
                text.AppendLine($"{model.Name}: rot {root.localRotation.eulerAngles} scale {root.localScale} bounds {bounds.min:F2}..{bounds.max:F2}" +
                                (avatar != null ? $" avatar valid={avatar.isValid} human={avatar.isHuman}" : ""));
                foreach (var t in model.Prefab.GetComponentsInChildren<Transform>())
                    if (t.name == "Hips" || t.name == "Head" || t.name == "LeftHand" || t.name == "MainHandGrip" || t.name == "LeftFoot")
                        text.AppendLine($"  {t.name} at {t.position:F2} rot {t.rotation.eulerAngles:F0}");
                foreach (var r in model.Prefab.GetComponentsInChildren<Renderer>())
                    text.AppendLine($"  renderer {r.name}: " + string.Join(", ", r.sharedMaterials.Select(m => m == null ? "null" : $"{m.name} {m.GetColor("_BaseColor")}")));
            }
            foreach (var clip in catalog.Clips)
                text.AppendLine($"  clip {clip.Set}/{clip.Name}: {clip.Clip.length:F2}s loop={clip.Clip.isLooping} human={clip.Clip.humanMotion}");
            // Where the hands end up in the humanoid idle pose (retargeted), to compare with Blender.
            var idle = catalog.GetClips(EntityVisuals.HumanoidClips);
            foreach (var model in catalog.Models)
            {
                var animator = model.Prefab.GetComponent<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman || !idle.TryGetValue("Idle", out var clip)) continue;
                var instance = Object.Instantiate(model.Prefab);
                AnimationMode.StartAnimationMode();
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(instance, clip, 0f);
                AnimationMode.EndSampling();
                foreach (var t in instance.GetComponentsInChildren<Transform>())
                    if (t.name == "MainHandGrip" || t.name == "RightHand" || t.name == "RightLowerArm")
                        text.AppendLine($"  idle {model.Name} {t.name} at {t.position:F2} forward {t.forward:F2} up {t.up:F2}");
                AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(instance);
            }
            Debug.Log(text.ToString());
        }

        public static void ReportFromCommandLine()
        {
            Report();
            EditorApplication.Exit(0);
        }
    }
}
