using System.Collections.Generic;
using Madlands.Shared.Game;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>Size of a display, for nameplates, selection rings and click targets.</summary>
    public struct DisplayShape
    {
        public float Height;
        public float Radius;
        /// <summary>Has hands: shows the weapon and shield its unit holds.</summary>
        public bool Hands;
    }

    /// <summary>
    /// A display built from a Blender model (art/README.md): its animator and clips, the weapon grips
    /// and the gear regions that can be recolored (body materials named Chest, Legs, ...).
    /// </summary>
    public sealed class DisplayModel
    {
        public Transform Root;
        public Animator Animator;
        public IReadOnlyDictionary<string, AnimationClip> Clips;
        public bool Humanoid;
        public Transform MainHandGrip;
        public Transform OffHandGrip;
        /// <summary>Weapon rotation relative to each grip: the weapon's +Y points forward in the rest pose.</summary>
        public Quaternion MainHandOffset;
        public Quaternion OffHandOffset;
        /// <summary>Top of the model in the rest pose (meters above its feet).</summary>
        public float Height;
        /// <summary>Ground speeds (m/s) at which the Walk and Run clips play at normal speed.</summary>
        public float WalkSpeed = 1.7f;
        public float RunSpeed = 6.2f;

        private static readonly Dictionary<(Material, Color), Material> Tinted = new Dictionary<(Material, Color), Material>();
        private Renderer _body;
        private Material[] _baseMaterials;

        public Renderer Body
        {
            get => _body;
            set
            {
                _body = value;
                _baseMaterials = value != null ? value.sharedMaterials : null;
            }
        }

        /// <summary>Recolors one gear region (a material of the body) of this model only.</summary>
        public void Tint(string region, Color color)
        {
            if (_body == null) return;
            var materials = _body.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                var source = _baseMaterials[i];
                if (source == null || source.name != region) continue;
                if (!Tinted.TryGetValue((source, color), out var tinted))
                {
                    tinted = new Material(source) { name = region + " (tinted)" };
                    tinted.SetColor("_BaseColor", color);
                    Tinted.Add((source, color), tinted);
                }
                materials[i] = tinted;
            }
            _body.sharedMaterials = materials;
        }

        /// <summary>The bone transform with this name (null if the model has none).</summary>
        public Transform Bone(string name) => Find(Root, name);

        private static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var found = Find(t.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Adds a primitive that follows a bone. Position, rotation and scale are in model space for the
        /// rest pose (meters, +Z forward), so attachments are placed the way the model was made.
        /// </summary>
        public Transform Attach(string bone, PrimitiveType type, Vector3 position, Vector3 scale, Color color, Vector3 euler = default, bool glow = false)
        {
            var parent = Bone(bone) ?? Root;
            // Placed in the space of the model's holder (the root itself is turned on import).
            var space = Root.parent;
            var part = glow ? EntityVisuals.Glow(space, position, scale, color, type) : EntityVisuals.Part(type, space, position, scale, color);
            part.localRotation = Quaternion.Euler(euler);
            part.SetParent(parent, true);
            return part;
        }
    }

    /// <summary>
    /// Art by the server's displayId (the server never names assets): Blender models from the
    /// <see cref="ArtCatalog"/>, recolored and dressed per display, and primitive placeholders for
    /// displays without a model yet. Later this becomes data (Addressables); the ids stay the same.
    /// </summary>
    public static class EntityVisuals
    {
        public const uint PlayerDisplay = 1;
        public const uint KorrinDisplay = 2;
        public const uint FrogDisplay = 101;
        public const uint WolfDisplay = 102;
        public const uint BanditDisplay = 103;
        public const uint ShardcasterDisplay = 104;
        public const uint DummyDisplay = 105;
        public const uint CaptainDisplay = 110;
        public const uint HerbalistDisplay = 111;
        public const uint QuartermasterDisplay = 112;
        public const uint InnkeeperDisplay = 113;
        public const uint FarmerDisplay = 114;
        public const uint ProspectorDisplay = 115;
        public const uint ScoutDisplay = 116;
        public const uint FishermanDisplay = 117;
        public const uint PriestDisplay = 118;
        public const uint SmithDisplay = 119;
        public const uint MirelingDisplay = 120;
        public const uint MirelingOracleDisplay = 121;
        public const uint DrownedDisplay = 122;
        public const uint LieutenantDisplay = 123;
        public const uint GloomjawDisplay = 124;
        public const uint SnapperDisplay = 125;
        public const uint BoarDisplay = 126;
        public const uint SpiderDisplay = 127;
        public const uint BearDisplay = 128;
        public const uint ShardlingDisplay = 129;

        /// <summary>The model file whose clips every humanoid race plays (retargeted by Unity).</summary>
        public const string HumanoidClips = "HumanoidAnimations";

        private static readonly Dictionary<Color, Material> LitMaterials = new Dictionary<Color, Material>();
        private static readonly Dictionary<Color, Material> GlowMaterials = new Dictionary<Color, Material>();
        private static Material _litTemplate;
        private static Material _unlitTemplate;

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        /// <summary>
        /// Builds the visual for a display under <paramref name="parent"/> (no colliders).
        /// <paramref name="model"/> is set when the display is an animated model.
        /// </summary>
        public static DisplayShape Create(Transform parent, uint displayId, out DisplayModel model)
        {
            model = null;
            DisplayShape shape;
            switch (displayId)
            {
                case PlayerDisplay:
                    return Humanoid(parent, "BrightvaleHuman", out model);
                case KorrinDisplay:
                    return Humanoid(parent, "Korrin", out model);
                case BanditDisplay:
                    // Red rags, a dark hood and a stolen shard glowing on the chest.
                    shape = Humanoid(parent, "BrightvaleHuman", out model);
                    if (model == null) return shape;
                    model.Tint("Chest", Hex("#7a2c22"));
                    model.Tint("Arms", Hex("#4a3a32"));
                    model.Tint("Legs", Hex("#3b3230"));
                    model.Tint("Hair", Hex("#2a2422"));
                    model.Tint("Feet", Hex("#2e2622"));
                    model.Attach("Head", PrimitiveType.Sphere, new Vector3(0f, 1.74f, -0.03f), new Vector3(0.27f, 0.26f, 0.3f), Hex("#2f2a28"));
                    model.Attach("Chest", PrimitiveType.Cube, new Vector3(0f, 1.32f, 0.13f), new Vector3(0.07f, 0.12f, 0.04f), new Color(0.4f, 0.95f, 1f), new Vector3(0f, 0f, 45f), glow: true);
                    return shape;
                case ShardcasterDisplay:
                {
                    // A violet robe and a crystal turning over the head.
                    shape = Humanoid(parent, "BrightvaleHuman", out model);
                    if (model == null) return shape;
                    model.Tint("Chest", Hex("#55357a"));
                    model.Tint("Arms", Hex("#55357a"));
                    model.Tint("Legs", Hex("#3e2a5a"));
                    model.Tint("Waist", Hex("#c9a23e"));
                    model.Tint("Hair", Hex("#d8d4cc"));
                    var crystal = Glow(parent, new Vector3(0f, shape.Height + 0.45f, 0f), new Vector3(0.2f, 0.34f, 0.2f), new Color(0.45f, 1f, 1f));
                    crystal.localRotation = Quaternion.Euler(0f, 45f, 0f);
                    crystal.gameObject.AddComponent<Spinner>();
                    shape.Height += 0.6f;
                    return shape;
                }
                case CaptainDisplay:
                {
                    // Steel armor, a helmet and a blue cloak: the outpost's commander.
                    shape = Humanoid(parent, "BrightvaleHuman", out model);
                    if (model == null) return shape;
                    var steel = Hex("#9aa3b2");
                    model.Tint("Chest", steel);
                    model.Tint("Arms", steel);
                    model.Tint("Hands", Hex("#6d7480"));
                    model.Tint("Legs", Hex("#2f3d66"));
                    model.Tint("Feet", Hex("#4a4f58"));
                    model.Attach("Head", PrimitiveType.Sphere, new Vector3(0f, 1.76f, 0f), new Vector3(0.27f, 0.2f, 0.29f), steel);
                    model.Attach("Head", PrimitiveType.Cube, new Vector3(0f, 1.69f, 0f), new Vector3(0.29f, 0.03f, 0.31f), Hex("#6d7480"));
                    model.Attach("Chest", PrimitiveType.Cube, new Vector3(0f, 1.0f, -0.15f), new Vector3(0.46f, 0.95f, 0.03f), Hex("#27428a"), new Vector3(-6f, 0f, 0f));
                    foreach (var x in new[] { -0.22f, 0.22f })
                        model.Attach("Chest", PrimitiveType.Sphere, new Vector3(x, 1.46f, 0f), new Vector3(0.2f, 0.12f, 0.22f), steel);
                    return shape;
                }
                case HerbalistDisplay:
                {
                    // Gray hair, a purple robe and a tall wide-brimmed hat.
                    shape = Humanoid(parent, "BrightvaleHuman", out model);
                    if (model == null) return shape;
                    model.Tint("Hair", Hex("#c8c4bc"));
                    model.Tint("Chest", Hex("#5a4370"));
                    model.Tint("Arms", Hex("#5a4370"));
                    model.Tint("Legs", Hex("#4a3a5c"));
                    var hat = Hex("#2c2433");
                    model.Attach("Head", PrimitiveType.Cylinder, new Vector3(0f, 1.8f, 0f), new Vector3(0.6f, 0.015f, 0.6f), hat);
                    model.Attach("Head", PrimitiveType.Cylinder, new Vector3(0f, 1.92f, 0f), new Vector3(0.27f, 0.12f, 0.27f), hat);
                    model.Attach("Head", PrimitiveType.Cylinder, new Vector3(0.03f, 2.08f, 0f), new Vector3(0.16f, 0.06f, 0.16f), hat, new Vector3(0f, 0f, -12f));
                    shape.Height += 0.3f;
                    return shape;
                }
                case FrogDisplay:
                    return Creature(parent, displayId, "Frog", 0.6f, walkSpeed: 1.2f, runSpeed: 3.5f, out model);
                case WolfDisplay:
                    return Creature(parent, displayId, "Wolf", 0.7f, walkSpeed: 1.3f, runSpeed: 4.2f, out model);
                case DummyDisplay:
                    return Creature(parent, displayId, "TrainingDummy", 0.55f, walkSpeed: 1f, runSpeed: 1f, out model);
                case QuartermasterDisplay:
                    // A broad Korrin in brown with a leather apron.
                    shape = Humanoid(parent, "Korrin", out model);
                    if (model == null) return shape;
                    model.Tint("Chest", Hex("#7a5636"));
                    model.Tint("Arms", Hex("#7a5636"));
                    model.Tint("Hair", Hex("#6b6158"));
                    model.Attach("Spine", PrimitiveType.Cube, new Vector3(0f, 0.86f, 0.17f), new Vector3(0.4f, 0.55f, 0.03f), Hex("#a88a62"));
                    return shape;
                case InnkeeperDisplay:
                    // Marla Fenn of the Muddy Boot: a wine-red dress and a white apron.
                    shape = Humanoid(parent, "BrightvaleHuman", out model);
                    if (model == null) return shape;
                    Dress(model, chest: "#7a3242", arms: "#7a3242", legs: "#5a2a36", hair: "#a3602a");
                    model.Attach("Spine", PrimitiveType.Cube, new Vector3(0f, 1.0f, 0.12f), new Vector3(0.3f, 0.6f, 0.02f), Hex("#e8e2d0"));
                    return shape;
                case FarmerDisplay:
                    // Wil Tenney: work clothes and a straw hat.
                    shape = Humanoid(parent, "BrightvaleHuman", out model);
                    if (model == null) return shape;
                    Dress(model, chest: "#6b7a48", arms: "#6b7a48", legs: "#5a4a35", hair: "#8a7d6c");
                    model.Attach("Head", PrimitiveType.Cylinder, new Vector3(0f, 1.81f, 0f), new Vector3(0.52f, 0.012f, 0.52f), Hex("#d8bf6a"));
                    model.Attach("Head", PrimitiveType.Cylinder, new Vector3(0f, 1.87f, 0f), new Vector3(0.25f, 0.07f, 0.25f), Hex("#d8bf6a"));
                    return shape;
                case ProspectorDisplay:
                    // Brannoc Ironvein: mail, a miner's helmet with a lamp.
                    shape = Humanoid(parent, "Korrin", out model);
                    if (model == null) return shape;
                    Dress(model, chest: "#5c616b", arms: "#5c616b", legs: "#4a3a2a", hair: "#3a2a1e");
                    model.Attach("Head", PrimitiveType.Sphere, new Vector3(0f, 1.42f, 0f), new Vector3(0.3f, 0.2f, 0.31f), Hex("#7d8490"));
                    model.Attach("Head", PrimitiveType.Cube, new Vector3(0f, 1.44f, 0.15f), new Vector3(0.06f, 0.06f, 0.04f), Hex("#ffd27a"), glow: true);
                    return shape;
                case ScoutDisplay:
                    // Rennick: forest greens and a hood.
                    shape = Humanoid(parent, "BrightvaleHuman", out model);
                    if (model == null) return shape;
                    Dress(model, chest: "#3d5a35", arms: "#3d5a35", legs: "#3a3428", hair: "#2a2420");
                    model.Attach("Head", PrimitiveType.Sphere, new Vector3(0f, 1.74f, -0.03f), new Vector3(0.28f, 0.27f, 0.31f), Hex("#2f4a2a"));
                    model.Attach("Chest", PrimitiveType.Cube, new Vector3(0f, 1.05f, -0.14f), new Vector3(0.42f, 0.9f, 0.03f), Hex("#2f4a2a"), new Vector3(-6f, 0f, 0f));
                    return shape;
                case FishermanDisplay:
                    // Old Pell: a Korrin in a blue jacket and an oilskin hat.
                    shape = Humanoid(parent, "Korrin", out model);
                    if (model == null) return shape;
                    Dress(model, chest: "#4a6a8a", arms: "#4a6a8a", legs: "#4a4438", hair: "#d0ccc4");
                    model.Attach("Head", PrimitiveType.Cylinder, new Vector3(0f, 1.47f, -0.02f), new Vector3(0.42f, 0.012f, 0.42f), Hex("#c9a640"));
                    model.Attach("Head", PrimitiveType.Sphere, new Vector3(0f, 1.49f, 0f), new Vector3(0.27f, 0.14f, 0.27f), Hex("#c9a640"));
                    return shape;
                case PriestDisplay:
                    // Brother Aldous: a white robe with a gold sash.
                    shape = Humanoid(parent, "BrightvaleHuman", out model);
                    if (model == null) return shape;
                    Dress(model, chest: "#ddd5bd", arms: "#ddd5bd", legs: "#cfc6ac", hair: "#7a6a5a", waist: "#c9a23e");
                    return shape;
                case SmithDisplay:
                    // Gerta Hask: bare arms, a leather apron.
                    shape = Humanoid(parent, "Korrin", out model);
                    if (model == null) return shape;
                    Dress(model, chest: "#6a4a3a", arms: "#d49a78", legs: "#4a3a2a", hair: "#c86a30");
                    model.Attach("Spine", PrimitiveType.Cube, new Vector3(0f, 0.86f, 0.17f), new Vector3(0.42f, 0.6f, 0.03f), Hex("#5a3a22"));
                    return shape;
                case MirelingDisplay:
                case MirelingOracleDisplay:
                {
                    // Fish-folk: a hunched Korrin body in teal scales, a fin crest and big glowing eyes.
                    shape = Humanoid(parent, "Korrin", out model);
                    if (model == null) return shape;
                    var scales = displayId == MirelingOracleDisplay ? "#5a7a9a" : "#4f8a7a";
                    Dress(model, chest: scales, arms: scales, legs: "#6a9a80", hair: "#2f5a4a", waist: displayId == MirelingOracleDisplay ? "#7a4a8a" : "#5a6a30",
                        skin: scales, hands: scales, feet: "#3f6a5a");
                    model.Attach("Head", PrimitiveType.Cube, new Vector3(0f, 1.48f, -0.02f), new Vector3(0.03f, 0.2f, 0.3f), Hex("#2f5a4a"));
                    foreach (var x in new[] { -0.07f, 0.07f })
                        model.Attach("Head", PrimitiveType.Sphere, new Vector3(x, 1.39f, 0.13f), Vector3.one * 0.07f, Hex("#f0e070"), glow: true);
                    if (displayId == MirelingOracleDisplay)
                        model.Attach("Chest", PrimitiveType.Sphere, new Vector3(0f, 1.0f, 0.17f), Vector3.one * 0.08f, Hex("#c8f0ff"), glow: true);
                    return shape;
                }
                case DrownedDisplay:
                    // The drowned dead of old Hollowmere: gray-green skin, rotten clothes, glowing eyes.
                    shape = Humanoid(parent, "BrightvaleHuman", out model);
                    if (model == null) return shape;
                    Dress(model, chest: "#3a4038", arms: "#8a9a8a", legs: "#2f352f", hair: "#2a3a2a", skin: "#8a9a8a", hands: "#8a9a8a", feet: "#2a2a2a");
                    foreach (var x in new[] { -0.045f, 0.045f })
                        model.Attach("Head", PrimitiveType.Cube, new Vector3(x, 1.72f, 0.126f), new Vector3(0.035f, 0.018f, 0.01f), Hex("#7ff2ff"), glow: true);
                    return shape;
                case LieutenantDisplay:
                {
                    // Lieutenant Vorn: black and red, a dark helmet, a head taller than his bandits.
                    shape = Humanoid(parent, "BrightvaleHuman", out model);
                    if (model == null) return shape;
                    var dark = Hex("#2a2a2c");
                    Dress(model, chest: "#5a1a1a", arms: "#2a2a2c", legs: "#2a2a2c", hair: "#1a1a1a", hands: "#2a2a2c");
                    model.Attach("Head", PrimitiveType.Sphere, new Vector3(0f, 1.77f, 0f), new Vector3(0.28f, 0.22f, 0.3f), Hex("#3a3c42"));
                    foreach (var x in new[] { -0.23f, 0.23f })
                        model.Attach("Chest", PrimitiveType.Sphere, new Vector3(x, 1.46f, 0f), new Vector3(0.22f, 0.14f, 0.24f), dark);
                    return Scaled(shape, model, 1.15f);
                }
                case GloomjawDisplay:
                    return Scaled(Creature(parent, displayId, "Gloomjaw", 1.2f, walkSpeed: 1f, runSpeed: 3f, out model), model, 3f);
                case SnapperDisplay:
                    return Scaled(Creature(parent, displayId, "Crocodile", 1.0f, walkSpeed: 1f, runSpeed: 3f, out model), model, 1.2f);
                case BoarDisplay:
                    return Creature(parent, displayId, "Boar", 0.7f, walkSpeed: 1.1f, runSpeed: 3.8f, out model);
                case SpiderDisplay:
                    return Scaled(Creature(parent, displayId, "Spider", 0.8f, walkSpeed: 1.2f, runSpeed: 3.5f, out model), model, 1.3f);
                case BearDisplay:
                    return Creature(parent, displayId, "Bear", 1.0f, walkSpeed: 1.4f, runSpeed: 4.5f, out model);
                case ShardlingDisplay:
                    return Creature(parent, displayId, "Shardling", 0.7f, walkSpeed: 1.5f, runSpeed: 4f, out model);
                default:
                    return Placeholder(parent, displayId);
            }
        }

        /// <summary>Recolors the gear regions of a humanoid (null keeps the model's color).</summary>
        private static void Dress(DisplayModel model, string chest = null, string arms = null, string legs = null, string hair = null,
            string waist = null, string skin = null, string hands = null, string feet = null)
        {
            if (chest != null) model.Tint("Chest", Hex(chest));
            if (arms != null) model.Tint("Arms", Hex(arms));
            if (legs != null) model.Tint("Legs", Hex(legs));
            if (hair != null) model.Tint("Hair", Hex(hair));
            if (waist != null) model.Tint("Waist", Hex(waist));
            if (skin != null) model.Tint("Skin", Hex(skin));
            if (hands != null) model.Tint("Hands", Hex(hands));
            if (feet != null) model.Tint("Feet", Hex(feet));
        }

        /// <summary>Scales a model (after its attachments are placed); bigger strides play slower.</summary>
        private static DisplayShape Scaled(DisplayShape shape, DisplayModel model, float scale)
        {
            if (model == null) return shape;
            model.Root.localScale *= scale;
            model.Height *= scale;
            model.WalkSpeed *= scale;
            model.RunSpeed *= scale;
            shape.Height *= scale;
            shape.Radius *= scale;
            return shape;
        }

        /// <summary>A humanoid race with the shared humanoid clips (a placeholder if the art is missing).</summary>
        private static DisplayShape Humanoid(Transform parent, string modelName, out DisplayModel model)
        {
            model = Instantiate(parent, modelName, HumanoidClips, humanoid: true);
            if (model == null) return Placeholder(parent, PlayerDisplay);
            return new DisplayShape { Height = model.Height, Radius = 0.45f, Hands = model.MainHandGrip != null };
        }

        /// <summary>A creature model with its own clips (a placeholder if the art is missing).</summary>
        private static DisplayShape Creature(Transform parent, uint displayId, string modelName, float radius, float walkSpeed, float runSpeed, out DisplayModel model)
        {
            model = Instantiate(parent, modelName, modelName, humanoid: false);
            if (model == null) return Placeholder(parent, displayId);
            model.WalkSpeed = walkSpeed;
            model.RunSpeed = runSpeed;
            return new DisplayShape { Height = model.Height, Radius = radius };
        }

        private static DisplayModel Instantiate(Transform parent, string modelName, string clipSet, bool humanoid)
        {
            var catalog = ArtCatalog.Instance;
            var prefab = catalog != null ? catalog.GetModel(modelName) : null;
            if (prefab == null) return null;
            var root = Object.Instantiate(prefab, parent, false).transform;
            root.name = modelName;
            var model = new DisplayModel
            {
                Root = root,
                Animator = root.GetComponent<Animator>(),
                Clips = catalog.GetClips(clipSet),
                Humanoid = humanoid,
                Body = root.GetComponentInChildren<SkinnedMeshRenderer>(),
            };
            if (model.Animator != null) model.Animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                model.Height = Mathf.Max(model.Height, r.bounds.max.y - root.position.y);
            if (model.Body is SkinnedMeshRenderer skinned)
            {
                // The bounds come from the rest pose; lying dead or swinging arms reach further.
                var bounds = skinned.localBounds;
                bounds.Expand(1f);
                skinned.localBounds = bounds;
            }
            // Grips in the rest pose: a weapon's +Y points forward, its +X up (blade edge up).
            model.MainHandGrip = model.Bone("MainHandGrip");
            model.OffHandGrip = model.Bone("OffHandGrip");
            var toModel = Quaternion.Inverse(parent.rotation);
            if (model.MainHandGrip != null)
                model.MainHandOffset = Quaternion.Inverse(toModel * model.MainHandGrip.rotation) * Quaternion.LookRotation(Vector3.right, Vector3.forward);
            if (model.OffHandGrip != null)
                model.OffHandOffset = Quaternion.Inverse(toModel * model.OffHandGrip.rotation) * Quaternion.LookRotation(Vector3.down, Vector3.forward);
            return model;
        }

        /// <summary>Primitive art for displays without a model yet.</summary>
        private static DisplayShape Placeholder(Transform parent, uint displayId)
        {
            switch (displayId)
            {
                case FrogDisplay:
                {
                    var skin = new Color(0.36f, 0.52f, 0.24f);
                    Part(PrimitiveType.Sphere, parent, new Vector3(0f, 0.32f, 0f), new Vector3(0.95f, 0.55f, 1.05f), skin);
                    Part(PrimitiveType.Sphere, parent, new Vector3(-0.22f, 0.58f, 0.32f), Vector3.one * 0.22f, new Color(0.95f, 0.9f, 0.6f));
                    Part(PrimitiveType.Sphere, parent, new Vector3(0.22f, 0.58f, 0.32f), Vector3.one * 0.22f, new Color(0.95f, 0.9f, 0.6f));
                    Part(PrimitiveType.Sphere, parent, new Vector3(-0.38f, 0.12f, -0.15f), new Vector3(0.3f, 0.2f, 0.5f), skin * 0.8f);
                    Part(PrimitiveType.Sphere, parent, new Vector3(0.38f, 0.12f, -0.15f), new Vector3(0.3f, 0.2f, 0.5f), skin * 0.8f);
                    return new DisplayShape { Height = 0.8f, Radius = 0.6f };
                }
                case WolfDisplay:
                {
                    var fur = new Color(0.4f, 0.4f, 0.45f);
                    var body = Part(PrimitiveType.Capsule, parent, new Vector3(0f, 0.75f, -0.1f), new Vector3(0.6f, 0.75f, 0.6f), fur);
                    body.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    Part(PrimitiveType.Sphere, parent, new Vector3(0f, 1.0f, 0.75f), new Vector3(0.45f, 0.42f, 0.55f), fur * 0.9f);
                    Part(PrimitiveType.Cube, parent, new Vector3(0f, 0.92f, 1.05f), new Vector3(0.2f, 0.16f, 0.3f), fur * 0.7f);
                    Part(PrimitiveType.Cube, parent, new Vector3(-0.13f, 1.25f, 0.7f), new Vector3(0.08f, 0.18f, 0.06f), fur * 0.7f);
                    Part(PrimitiveType.Cube, parent, new Vector3(0.13f, 1.25f, 0.7f), new Vector3(0.08f, 0.18f, 0.06f), fur * 0.7f);
                    var tail = Part(PrimitiveType.Cylinder, parent, new Vector3(0f, 0.85f, -0.95f), new Vector3(0.1f, 0.3f, 0.1f), fur * 0.8f);
                    tail.localRotation = Quaternion.Euler(-60f, 0f, 0f);
                    foreach (var x in new[] { -0.2f, 0.2f })
                        foreach (var z in new[] { -0.45f, 0.35f })
                            Part(PrimitiveType.Cylinder, parent, new Vector3(x, 0.25f, z), new Vector3(0.12f, 0.25f, 0.12f), fur * 0.75f);
                    return new DisplayShape { Height = 1.3f, Radius = 0.8f };
                }
                case DummyDisplay:
                {
                    var wood = new Color(0.45f, 0.31f, 0.18f);
                    Part(PrimitiveType.Cylinder, parent, new Vector3(0f, 0.05f, 0f), new Vector3(0.9f, 0.05f, 0.9f), wood * 0.8f);
                    Part(PrimitiveType.Cylinder, parent, new Vector3(0f, 1.0f, 0f), new Vector3(0.16f, 1.0f, 0.16f), wood);
                    Part(PrimitiveType.Cube, parent, new Vector3(0f, 1.45f, 0f), new Vector3(1.3f, 0.12f, 0.12f), wood);
                    Part(PrimitiveType.Capsule, parent, new Vector3(0f, 1.35f, 0f), new Vector3(0.6f, 0.45f, 0.45f), new Color(0.82f, 0.72f, 0.42f));
                    Part(PrimitiveType.Sphere, parent, new Vector3(0f, 2.0f, 0f), Vector3.one * 0.45f, new Color(0.86f, 0.78f, 0.5f));
                    return new DisplayShape { Height = 2.2f, Radius = 0.55f };
                }
                default:
                    // The Phase 1 capsule with a "nose" that shows the facing.
                    Part(PrimitiveType.Capsule, parent, new Vector3(0f, 1f, 0f), new Vector3(0.8f, 1f, 0.8f), Color.gray);
                    Part(PrimitiveType.Cube, parent, new Vector3(0f, 1.55f, 0.38f), new Vector3(0.25f, 0.15f, 0.3f), Color.gray * 0.5f);
                    return new DisplayShape { Height = 2.0f, Radius = 0.45f, Hands = true };
            }
        }

        public static Transform Part(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = LitMaterial(color);
            return go.transform;
        }

        /// <summary>A self-lit part (crystals, projectiles): stays bright in shadow.</summary>
        public static Transform Glow(Transform parent, Vector3 position, Vector3 scale, Color color, PrimitiveType type = PrimitiveType.Cube)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = GlowMaterial(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        public static Material LitMaterial(Color color)
        {
            // A URP material from Resources/ is guaranteed to be in player builds; the primitives'
            // built-in default material is not URP-compatible there.
            if (_litTemplate == null) _litTemplate = Resources.Load<Material>("EntityPlaceholder");
            if (!LitMaterials.TryGetValue(color, out var material))
            {
                material = new Material(_litTemplate) { color = color };
                LitMaterials.Add(color, material);
            }
            return material;
        }

        public static Material GlowMaterial(Color color)
        {
            if (_unlitTemplate == null) _unlitTemplate = Resources.Load<Material>("Unlit");
            if (!GlowMaterials.TryGetValue(color, out var material))
            {
                material = new Material(_unlitTemplate != null ? _unlitTemplate : LitMaterial(color)) { color = color };
                GlowMaterials.Add(color, material);
            }
            return material;
        }
    }

    /// <summary>Colors per spell school: icons, projectiles, cast glows.</summary>
    public static class SchoolColors
    {
        public static Color Get(SpellSchool school)
        {
            switch (school)
            {
                case SpellSchool.Arcane: return new Color(0.72f, 0.45f, 1f);
                case SpellSchool.Fire: return new Color(1f, 0.5f, 0.15f);
                case SpellSchool.Frost: return new Color(0.45f, 0.8f, 1f);
                case SpellSchool.Nature: return new Color(0.4f, 0.88f, 0.35f);
                case SpellSchool.Holy: return new Color(1f, 0.9f, 0.45f);
                case SpellSchool.Shadow: return new Color(0.55f, 0.3f, 0.7f);
                default: return new Color(0.8f, 0.72f, 0.6f);
            }
        }
    }

    /// <summary>Slowly turns floating crystals.</summary>
    public sealed class Spinner : MonoBehaviour
    {
        private void Update()
        {
            transform.Rotate(0f, 90f * Time.deltaTime, 0f, Space.World);
            var p = transform.localPosition;
            p.y += Mathf.Sin(Time.time * 2f) * 0.002f;
            transform.localPosition = p;
        }
    }
}
