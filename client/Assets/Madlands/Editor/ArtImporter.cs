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

        /// <summary>Clips that repeat; everything else plays once.</summary>
        public static readonly HashSet<string> LoopingClips = new HashSet<string>
        {
            "Idle", "Walk", "Run", "RunBack", "StrafeLeft", "StrafeRight", "Fall", "CastLoop", "Sit", "Swim",
        };

        /// <summary>The humanoid bones every race has, under Unity's own names.</summary>
        private static readonly string[] HumanBones =
        {
            "Hips", "Spine", "Chest", "Neck", "Head",
            "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
            "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes", "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes",
        };

        private bool IsArt => assetPath.StartsWith(ArtRoot, System.StringComparison.Ordinal);

        public override int GetPostprocessOrder() => 10; // after URP's own material conversion

        /// <summary>Raise when the import rules change: Unity then imports the art again with them.</summary>
        public override uint GetVersion() => 11;

        /// <summary>Icons/ are interface pictures (art/blender/icons.py): kept sharp and transparent when drawn small.</summary>
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtRoot + "Icons/", System.StringComparison.Ordinal)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }

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
                if (character)
                {
                    // Our bones carry Unity's humanoid names (art/blender/humanoid.py): map every one of them.
                    // Unity's own guess leaves out the chest and shoulders, and their rotations in the clips are
                    // then dropped. The skeleton (the T-pose) is the one measured last: RemeasureSkeletons measures
                    // it again when the proportions change. The animation-only file gets its own avatar too: it
                    // is exported in the rest pose of the skeleton it was made on (lowpoly.export_fbx).
                    var description = importer.humanDescription;
                    description.human = HumanMapping();
                    importer.humanDescription = description;
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
            if (assetPath.Contains("/Characters/"))
            {
                EditorApplication.delayCall -= RemeasureSkeletons;
                EditorApplication.delayCall += RemeasureSkeletons;
            }
        }

        private static HumanBone[] HumanMapping() => HumanBones.Select(name => new HumanBone
        {
            boneName = name,
            humanName = name,
            limit = new HumanLimit { useDefaultValues = true },
        }).ToArray();

        /// <summary>
        /// A character's avatar is measured from the skeleton stored with its importer. When a model's bones
        /// moved (new proportions in Blender), stores the new skeleton and imports the model again, so the avatar
        /// fits. The editor runs this after importing characters; builds run it first (batch mode may never
        /// get to the editor's delayed calls).
        /// </summary>
        public static void RemeasureSkeletons()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ArtRoot + "Characters" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!(AssetImporter.GetAtPath(path) is ModelImporter importer) || model == null) continue;

                var stored = importer.humanDescription.skeleton ?? new SkeletonBone[0];
                var measured = new List<SkeletonBone>
                {
                    stored.Length > 0 ? stored[0] : new SkeletonBone { name = model.name, rotation = Quaternion.identity, scale = Vector3.one },
                };
                string changed = stored.Length == 0 ? "no skeleton yet" : null;
                foreach (var t in model.GetComponentsInChildren<Transform>(true))
                {
                    if (t == model.transform) continue;
                    // Positions only: Unity keeps its own T-pose rotations in the stored skeleton.
                    int i = System.Array.FindIndex(stored, b => b.name == t.name);
                    var rotation = i >= 0 ? stored[i].rotation : t.localRotation;
                    measured.Add(new SkeletonBone { name = t.name, position = t.localPosition, rotation = rotation, scale = t.localScale });
                    if (changed != null) continue;
                    if (i < 0) changed = t.name + " is new";
                    else if ((stored[i].position - t.localPosition).magnitude > 0.001f)
                        changed = $"{t.name} moved from {stored[i].position:F3} to {t.localPosition:F3}";
                }
                // A new model's first import has no skeleton to map with: Unity then guesses the mapping itself
                // (without the chest and shoulders).
                var mapped = importer.humanDescription.human ?? new HumanBone[0];
                if (changed == null && HumanBones.Any(name => !mapped.Any(b => b.humanName == name && b.boneName == name)))
                    changed = "not every bone is mapped by name";
                if (changed == null) continue;

                Debug.Log($"[ArtImporter] {path}: the skeleton changed ({changed}), measuring the avatar again");
                var description = importer.humanDescription;
                description.human = HumanMapping();
                description.skeleton = measured.ToArray();
                importer.humanDescription = description;
                importer.SaveAndReimport();
            }
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
            catalog.Icons.Clear();
            foreach (var path in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtRoot + "Icons" })
                         .Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(p => p, System.StringComparer.Ordinal))
            {
                var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (icon != null) catalog.Icons.Add(new ArtCatalog.IconEntry { Name = Path.GetFileNameWithoutExtension(path), Texture = icon });
            }
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
            Debug.Log($"[ArtImporter] Art catalog: {catalog.Models.Count} models, {catalog.Clips.Count} clips, {catalog.Icons.Count} icons");
        }

        /// <summary>
        /// Logs what Unity made of each model (size, avatar, materials, clips), for checking new art from
        /// the command line: -executeMethod Madlands.Client.Editor.ArtImporter.ReportFromCommandLine
        /// </summary>
        [MenuItem("Madlands/Art Report")]
        public static void Report()
        {
            RemeasureSkeletons();
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
                                (avatar != null ? $" avatar valid={avatar.isValid} human={avatar.isHuman}" : "") +
                                (avatar != null && avatar.isHuman ? $" bones={string.Join(",", avatar.humanDescription.human.Select(b => b.humanName))}" : ""));
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
                // Under a holder, as in the game (EntityVisuals): the holder faces +Z, its model faces the same way.
                var holder = new GameObject("Holder").transform;
                var instance = Object.Instantiate(model.Prefab, holder, false);
                var grip = instance.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "MainHandGrip");
                var gripRest = grip != null ? grip.rotation : Quaternion.identity;
                var rootRest = instance.transform.rotation;
                AnimationMode.StartAnimationMode();
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(instance, clip, 0f);
                AnimationMode.EndSampling();
                foreach (var t in instance.GetComponentsInChildren<Transform>())
                    if (t.name == "MainHandGrip" || t.name == "RightHand" || t.name == "RightLowerArm")
                        text.AppendLine($"  idle {model.Name} {t.name} at {t.position:F2} forward {t.forward:F2} up {t.up:F2}");
                // Where a held weapon points, as seen from the holder (+Z forward, +X right, +Y up): it points
                // forward in the rest pose. Sampling moves the root, so the pose is taken relative to it.
                if (grip != null)
                {
                    var weapon = rootRest * Quaternion.Inverse(instance.transform.rotation) * grip.rotation * Quaternion.Inverse(gripRest) * Vector3.forward;
                    text.AppendLine($"  idle {model.Name} weapon points {weapon:F2} (forward +Z, right +X, up +Y)");
                }
                AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(holder.gameObject);
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
