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
    /// A display built from a Blender model (art/README.md): its animator and clips, the weapon grips,
    /// the gear regions that can be recolored (body materials named Chest, Legs, ...) and the worn gear
    /// pieces the model carries, hidden until shown (meshes named "Gear" + piece: art/blender/gear.py).
    /// A character is several meshes (body, hair, beard, pieces), each in up to three levels of detail
    /// (names ending in _LOD0, _LOD1, _LOD2) that a LOD group switches between.
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

        /// <summary>How much darker a region's second tone (its "Trim" material: collars, cuffs, soles) is.</summary>
        private const float TrimShade = 0.62f;
        /// <summary>How much darker a gear piece's "Dark" material (linings, the inside of a hood) is.</summary>
        private const float DarkShade = 0.38f;
        private const string PiecePrefix = "Gear";

        private static readonly Dictionary<(Material, Color), Material> Tinted = new Dictionary<(Material, Color), Material>();
        private static Material _invisible;
        private static bool _invisibleLoaded;
        /// <summary>The meshes that are always shown (body, hair, beard) at every level of detail.</summary>
        private readonly List<BodyPart> _parts = new List<BodyPart>();
        private readonly Dictionary<string, Color?> _tints = new Dictionary<string, Color?>();
        private readonly HashSet<string> _hidden = new HashSet<string>();
        /// <summary>Gear pieces by name ("Hood"), one renderer per level of detail.</summary>
        private readonly Dictionary<string, List<Piece>> _pieces = new Dictionary<string, List<Piece>>();
        private Transform _cloak;
        private Quaternion _cloakRest;
        private Vector3 _cloakRestPosition;
        private Vector3 _cloakAxis;
        private float _cloakAngle;

        /// <summary>A mesh of the body: its renderer and the model's own materials.</summary>
        private sealed class BodyPart
        {
            public Renderer Renderer;
            public Material[] Materials;
        }

        /// <summary>A gear piece at one level of detail: its renderer, its own materials and how high it reaches in the rest pose.</summary>
        private sealed class Piece
        {
            public Renderer Renderer;
            public Material[] Materials;
            public float Top;
        }

        /// <summary>A mesh's name without its level of detail ("GearHood_LOD1" -> "GearHood").</summary>
        public static string BaseName(string name)
        {
            int i = name.LastIndexOf("_LOD", System.StringComparison.Ordinal);
            return i > 0 && i + 4 < name.Length && char.IsDigit(name[i + 4]) ? name.Substring(0, i) : name;
        }

        /// <summary>The level of detail in a mesh's name (0 without one).</summary>
        public static int LodLevel(string name)
        {
            int i = name.LastIndexOf("_LOD", System.StringComparison.Ordinal);
            return i > 0 && i + 4 < name.Length && int.TryParse(name.Substring(i + 4), out int level) ? level : 0;
        }

        /// <summary>Takes a mesh of the body (body, hair, beard): tints and hidden regions apply to its materials.</summary>
        public void AddBodyPart(Renderer renderer)
        {
            _parts.Add(new BodyPart { Renderer = renderer, Materials = renderer.sharedMaterials });
        }

        public bool HasBody => _parts.Count > 0;

        /// <summary>Back to the model's own color for a region (gear taken off).</summary>
        public void ResetTint(string region)
        {
            if (_tints.Remove(region)) ApplyMaterials();
        }

        /// <summary>
        /// Recolors one gear region of this model only: the body materials named <paramref name="region"/>
        /// (and region + "Lower"), its darker "Trim" second tone, and for "Hair" also the beard and brows.
        /// </summary>
        public void Tint(string region, Color color)
        {
            _tints[region] = color;
            ApplyMaterials();
        }

        /// <summary>Hides a region (a body material), like the hair under a helmet, or shows it again.</summary>
        public void SetHidden(string region, bool hidden)
        {
            if (hidden ? _hidden.Add(region) : _hidden.Remove(region)) ApplyMaterials();
        }

        /// <summary>
        /// Takes a gear piece the model carries (art/blender/gear.py) and keeps it hidden until it is worn.
        /// <paramref name="top"/> is how high it reaches in the rest pose.
        /// </summary>
        public void AddPiece(Renderer renderer, float top)
        {
            string name = BaseName(renderer.gameObject.name).Substring(PiecePrefix.Length);
            if (!_pieces.TryGetValue(name, out var lods)) _pieces[name] = lods = new List<Piece>();
            lods.Add(new Piece { Renderer = renderer, Materials = renderer.sharedMaterials, Top = top });
            renderer.gameObject.SetActive(false);
        }

        public static bool IsPiece(Renderer renderer) => renderer.gameObject.name.StartsWith(PiecePrefix, System.StringComparison.Ordinal);

        /// <summary>
        /// Shows a gear piece in a color: its "Gear" materials take the color ("GearTrim" a darker tone,
        /// "GearDark" a much darker one), the rest keep theirs (rivets, bone, a lamp). False if the model
        /// has no such piece.
        /// </summary>
        public bool ShowPiece(string name, Color color)
        {
            if (!_pieces.TryGetValue(name, out var lods)) return false;
            foreach (var piece in lods)
            {
                var shown = new Material[piece.Materials.Length];
                for (int i = 0; i < shown.Length; i++)
                {
                    var source = piece.Materials[i];
                    if (source == null || !source.name.StartsWith(PiecePrefix, System.StringComparison.Ordinal))
                    {
                        shown[i] = source;
                        continue;
                    }
                    float shade = source.name.EndsWith("Trim", System.StringComparison.Ordinal) ? TrimShade
                        : source.name.EndsWith("Dark", System.StringComparison.Ordinal) ? DarkShade : 1f;
                    shown[i] = TintedCopy(source, new Color(color.r * shade, color.g * shade, color.b * shade, 1f));
                }
                piece.Renderer.sharedMaterials = shown;
                piece.Renderer.gameObject.SetActive(true);
                Height = Mathf.Max(Height, piece.Top * Root.localScale.y);
            }
            return true;
        }

        public void HidePieces()
        {
            foreach (var lods in _pieces.Values)
                foreach (var piece in lods)
                    piece.Renderer.gameObject.SetActive(false);
        }

        /// <summary>
        /// Groups the model's renderers by their level of detail (names ending in _LODn) into a LOD group:
        /// full detail up close, the coarser meshes further away. Models without levels are left alone.
        /// </summary>
        public void SetUpLods(IEnumerable<Renderer> renderers)
        {
            var byLevel = new SortedDictionary<int, List<Renderer>>();
            foreach (var r in renderers)
            {
                int level = LodLevel(r.gameObject.name);
                if (!byLevel.TryGetValue(level, out var list)) byLevel[level] = list = new List<Renderer>();
                list.Add(r);
            }
            if (byLevel.Count < 2) return;
            // Screen heights (of the model's height) below which the next level takes over; the last culls.
            float[] below = { 0.14f, 0.045f, 0.004f };
            var lods = new List<LOD>();
            int k = 0;
            foreach (var pair in byLevel)
            {
                lods.Add(new LOD(below[Mathf.Min(k, below.Length - 1)], pair.Value.ToArray()));
                k++;
            }
            var group = Root.GetComponent<LODGroup>();
            if (group == null) group = Root.gameObject.AddComponent<LODGroup>();
            group.SetLODs(lods.ToArray());
            group.localReferencePoint = new Vector3(0f, Height * 0.5f / Mathf.Max(Root.lossyScale.y, 1e-4f), 0f);
            group.size = Mathf.Max(Height, 0.5f) / Mathf.Max(Root.lossyScale.y, 1e-4f);
        }

        /// <summary>
        /// Swings a cloak's hem (the Cloak bone) back with the speed the unit runs at, and out behind it
        /// while it sits; called after the animation each frame.
        /// </summary>
        public void SwayCloak(float dt, float speed, bool sitting)
        {
            if (_cloak == null) return;
            float target = sitting ? 55f : Mathf.Clamp(speed * 5.5f, 0f, 36f);
            target += speed > 0.5f ? Mathf.Sin(Time.time * 9f) * 3f : Mathf.Sin(Time.time * 1.3f) * 1.5f;
            _cloakAngle = Mathf.Lerp(_cloakAngle, target, 1f - Mathf.Exp(-dt * 6f));
            _cloak.localPosition = _cloakRestPosition;
            _cloak.localRotation = _cloakRest * Quaternion.AngleAxis(_cloakAngle, _cloakAxis);
        }

        /// <summary>Remembers the Cloak bone's rest pose (call with the rest pose captured).</summary>
        public void FindCloak()
        {
            _cloak = Bone("Cloak");
            if (_cloak == null || !_restFromModel.TryGetValue(_cloak.name, out var fromModel)) return;
            _cloakRest = _cloak.localRotation;
            _cloakRestPosition = _cloak.localPosition;
            // The model's right (+X): turning about it by a positive angle moves the hem back (-Z).
            _cloakAxis = fromModel.MultiplyVector(Vector3.right).normalized;
        }

        /// <summary>The material a body material is shown with: tinted by its region's color, or the model's own.</summary>
        private Material Shown(Material source)
        {
            if (source == null) return null;
            string name = source.name;
            foreach (var pair in _tints)
            {
                if (!pair.Value.HasValue) continue;
                string region = pair.Key;
                float shade;
                if (name == region || name == region + "Lower" || (region == "Hair" && name == "Beard")) shade = 1f;
                else if (name == region + "Trim") shade = TrimShade;
                else continue;
                var c = pair.Value.Value;
                return TintedCopy(source, new Color(c.r * shade, c.g * shade, c.b * shade, 1f));
            }
            return source;
        }

        private static Material TintedCopy(Material source, Color color)
        {
            if (!Tinted.TryGetValue((source, color), out var tinted))
            {
                tinted = new Material(source) { name = source.name + " (tinted)" };
                tinted.SetColor("_BaseColor", color);
                Tinted.Add((source, color), tinted);
            }
            return tinted;
        }

        private void ApplyMaterials()
        {
            foreach (var part in _parts)
            {
                var materials = new Material[part.Materials.Length];
                for (int i = 0; i < materials.Length; i++)
                {
                    var source = part.Materials[i];
                    materials[i] = source != null && _hidden.Contains(source.name) && Invisible != null ? Invisible : Shown(source);
                }
                part.Renderer.sharedMaterials = materials;
            }
        }

        private static Material Invisible
        {
            get
            {
                if (!_invisibleLoaded)
                {
                    _invisibleLoaded = true;
                    var shader = Resources.Load<Shader>("Invisible");
                    if (shader != null) _invisible = new Material(shader) { name = "Invisible" };
                    else Debug.LogWarning("[EntityVisuals] Resources/Invisible shader missing: hidden regions stay visible");
                }
                return _invisible;
            }
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

        /// <summary>Model space (the holder's space, +Z forward) to each bone's space, in the rest pose.</summary>
        private readonly Dictionary<string, Matrix4x4> _restFromModel = new Dictionary<string, Matrix4x4>();

        /// <summary>Remembers the rest pose; called before anything animates the model.</summary>
        public void CaptureRestPose()
        {
            var holder = Root.parent;
            foreach (var t in Root.GetComponentsInChildren<Transform>())
                _restFromModel[t.name] = t.worldToLocalMatrix * holder.localToWorldMatrix;
        }

        /// <summary>Where a bone sits in model space in the rest pose (zero if there is no such bone).</summary>
        public Vector3 RestPosition(string bone) =>
            _restFromModel.TryGetValue(bone, out var m) ? m.inverse.MultiplyPoint3x4(Vector3.zero) : Vector3.zero;

        /// <summary>
        /// Adds a primitive that follows a bone. Position, rotation and scale are in model space for the
        /// rest pose (meters, +Z forward), so attachments are placed the way the model was made, whatever
        /// pose the model is in now.
        /// </summary>
        public Transform Attach(string bone, PrimitiveType type, Vector3 position, Vector3 scale, Color color, Vector3 euler = default, bool glow = false)
        {
            var parent = Bone(bone) ?? Root;
            var part = glow ? EntityVisuals.Glow(parent, Vector3.zero, scale, color, type) : EntityVisuals.Part(type, parent, Vector3.zero, scale, color);
            var m = _restFromModel.TryGetValue(parent.name, out var found) ? found : Matrix4x4.identity;
            part.localPosition = m.MultiplyPoint3x4(position);
            part.localRotation = m.rotation * Quaternion.Euler(euler);
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
        public const uint SlagbornDisplay = 130;

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
            var shape = Build(parent, displayId, out model);
            // Hats and helmets make a head taller (nameplates, click targets).
            if (model != null) shape.Height = Mathf.Max(shape.Height, model.Height);
            return shape;
        }

        private static DisplayShape Build(Transform parent, uint displayId, out DisplayModel model)
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
                    Wear(model, "#2f2a28", "Hood");
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
                    Wear(model, "#55357a", "RobeSkirt", "RobeCuffs");
                    var crystal = Glow(parent, new Vector3(0f, shape.Height + 0.45f, 0f), new Vector3(0.2f, 0.34f, 0.2f), new Color(0.45f, 1f, 1f));
                    crystal.localRotation = Quaternion.Euler(0f, 45f, 0f);
                    crystal.gameObject.AddComponent<Spinner>();
                    shape.Height += 0.6f;
                    return shape;
                }
                case CaptainDisplay:
                {
                    // Gilded steel with Mirewatch's star, a helmet, a blue tabard and cloak: the outpost's commander.
                    shape = Humanoid(parent, "BrightvaleHuman", out model);
                    if (model == null) return shape;
                    Dress(model, chest: "#3a4a6e", arms: "#3b3c44", legs: "#2f3d66", hands: "#6d7480", feet: "#4a4f58");
                    Wear(model, "#9aa3b2", "Helm", "Pauldrons", "Breastplate", "StarEmblem", "Gauntlets", "Greaves");
                    Wear(model, "#27428a", "Cloak", "Tabard");
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
                    Wear(model, "#5a4370", "RobeSkirt", "RobeCuffs");
                    Wear(model, "#2c2433", "WizardHat");
                    return shape;
                }
                case FrogDisplay:
                    // "Fat as dogs, they are" (Old Tamsin).
                    return Scaled(Creature(parent, displayId, "Frog", 0.6f, walkSpeed: 1.2f, runSpeed: 3.5f, out model), model, 1.5f);
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
                    Wear(model, "#a88a62", "Apron");
                    return shape;
                case InnkeeperDisplay:
                    // Marla Fenn of the Muddy Boot: a wine-red dress and a white apron.
                    shape = Humanoid(parent, "BrightvaleHuman", out model);
                    if (model == null) return shape;
                    Dress(model, chest: "#7a3242", arms: "#7a3242", legs: "#5a2a36", hair: "#a3602a");
                    Wear(model, "#7a3242", "RobeSkirt");
                    Wear(model, "#e8e2d0", "Apron");
                    return shape;
                case FarmerDisplay:
                    // Wil Tenney: work clothes and a straw hat.
                    shape = Humanoid(parent, "BrightvaleHuman", out model);
                    if (model == null) return shape;
                    Dress(model, chest: "#6b7a48", arms: "#6b7a48", legs: "#5a4a35", hair: "#8a7d6c");
                    Wear(model, "#d8bf6a", "StrawHat");
                    return shape;
                case ProspectorDisplay:
                    // Brannoc Ironvein: mail, a miner's helmet with a lamp.
                    shape = Humanoid(parent, "Korrin", out model);
                    if (model == null) return shape;
                    Dress(model, chest: "#5c616b", arms: "#5c616b", legs: "#4a3a2a", hair: "#3a2a1e");
                    Wear(model, "#7d8490", "MinerHelm");
                    return shape;
                case ScoutDisplay:
                    // Rennick: forest greens and a hood.
                    shape = Humanoid(parent, "BrightvaleHuman", out model);
                    if (model == null) return shape;
                    Dress(model, chest: "#3d5a35", arms: "#3d5a35", legs: "#3a3428", hair: "#2a2420");
                    Wear(model, "#2f4a2a", "Hood", "Cloak");
                    return shape;
                case FishermanDisplay:
                    // Old Pell: a Korrin in a blue jacket and an oilskin hat.
                    shape = Humanoid(parent, "Korrin", out model);
                    if (model == null) return shape;
                    Dress(model, chest: "#4a6a8a", arms: "#4a6a8a", legs: "#4a4438", hair: "#d0ccc4");
                    Wear(model, "#c9a640", "OilskinHat");
                    return shape;
                case PriestDisplay:
                    // Brother Aldous: a white robe with a gold sash.
                    shape = Humanoid(parent, "BrightvaleHuman", out model);
                    if (model == null) return shape;
                    Dress(model, chest: "#ddd5bd", arms: "#ddd5bd", legs: "#cfc6ac", hair: "#7a6a5a", waist: "#c9a23e");
                    Wear(model, "#ddd5bd", "RobeSkirt", "RobeCuffs");
                    return shape;
                case SmithDisplay:
                    // Gerta Hask: bare arms, a leather apron, her hair in a topknot (and no beard).
                    shape = Humanoid(parent, "Korrin", out model);
                    if (model == null) return shape;
                    model.SetHidden("Beard", true);
                    model.SetHidden("BeardMetal", true);
                    Dress(model, chest: "#6a4a3a", arms: "#d49a78", legs: "#4a3a2a", hair: "#c86a30");
                    Wear(model, "#5a3a22", "Apron");
                    return shape;
                case MirelingDisplay:
                case MirelingOracleDisplay:
                {
                    // Fish-folk in teal scales with glowing eyes and fins (their fins take the hair color); the
                    // oracles wear a robe and carry a light on the chest.
                    shape = Humanoid(parent, "Mireling", out model);
                    if (model == null) return shape;
                    bool oracle = displayId == MirelingOracleDisplay;
                    var scales = oracle ? "#5a7a9a" : "#4f8a7a";
                    Dress(model, chest: scales, arms: scales, legs: "#6a9a80", hair: oracle ? "#3a4a6a" : "#2f5a4a", waist: oracle ? "#7a4a8a" : "#5a6a30",
                        skin: scales, hands: scales, feet: "#3f6a5a");
                    if (oracle)
                    {
                        Wear(model, "#4a3a6a", "RobeSkirt", "RobeCuffs");
                        model.Attach("Chest", PrimitiveType.Sphere, new Vector3(0f, 0.98f, 0.19f), Vector3.one * 0.08f, Hex("#c8f0ff"), glow: true);
                    }
                    return shape;
                }
                case DrownedDisplay:
                    // The drowned dead of old Hollowmere: gray-green skin, rotten clothes, glowing eyes.
                    shape = Humanoid(parent, "BrightvaleHuman", out model);
                    if (model == null) return shape;
                    Dress(model, chest: "#3a4038", arms: "#8a9a8a", legs: "#2f352f", hair: "#2a3a2a", skin: "#8a9a8a", hands: "#8a9a8a", feet: "#2a2a2a");
                    Wear(model, "#4a4a3a", "Rags");
                    foreach (var x in new[] { -0.044f, 0.044f })
                        model.Attach("Head", PrimitiveType.Cube, new Vector3(x, 1.706f, 0.121f), new Vector3(0.036f, 0.02f, 0.01f), Hex("#7ff2ff"), glow: true);
                    return shape;
                case LieutenantDisplay:
                {
                    // Lieutenant Vorn: black and red, a dark helmet, a head taller than his bandits.
                    shape = Humanoid(parent, "BrightvaleHuman", out model);
                    if (model == null) return shape;
                    Dress(model, chest: "#5a1a1a", arms: "#2a2a2c", legs: "#2a2a2c", hair: "#1a1a1a", hands: "#2a2a2c");
                    Wear(model, "#3a3c42", "HornedHelm", "SpikedPauldrons");
                    Wear(model, "#5a1a1a", "Cloak");
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
                case SlagbornDisplay:
                {
                    // Ruukhar the Slagborn: the brute of the Cinder Cut, lit by his own molten seams.
                    shape = Creature(parent, displayId, "Slagborn", 1.0f, walkSpeed: 1.3f, runSpeed: 3f, out model);
                    if (model == null) return shape;
                    var glow = new GameObject("MoltenGlow").AddComponent<Light>();
                    glow.transform.SetParent(model.Root, false);
                    glow.transform.localPosition = new Vector3(0f, 1.5f, 0.5f);
                    glow.type = LightType.Point;
                    glow.color = Hex("#ff7a30");
                    glow.range = 7f;
                    glow.intensity = 1.6f;
                    glow.shadows = LightShadows.None;
                    return Scaled(shape, model, 1.35f);
                }
                default:
                    return Placeholder(parent, displayId);
            }
        }

        /// <summary>Gear pieces in one color (GearVisuals.Wear).</summary>
        private static void Wear(DisplayModel model, string color, params string[] pieces)
        {
            foreach (var piece in pieces) GearVisuals.Wear(model, piece, Hex(color));
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
            };
            if (model.Animator != null) model.Animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            model.CaptureRestPose();
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                float top = r.bounds.max.y - root.position.y;
                if (r is SkinnedMeshRenderer skinned)
                {
                    // The bounds come from the rest pose; lying dead, swinging arms or a cloak reach further.
                    var bounds = skinned.localBounds;
                    bounds.Expand(1f);
                    skinned.localBounds = bounds;
                }
                if (DisplayModel.IsPiece(r))
                {
                    model.AddPiece(r, top);
                    continue;
                }
                if (r is SkinnedMeshRenderer) model.AddBodyPart(r);
                model.Height = Mathf.Max(model.Height, top);
            }
            model.SetUpLods(renderers);
            model.FindCloak();
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
