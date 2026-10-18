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
    /// Placeholder art, looked up by the server's displayId (the server never names assets). In
    /// Phase 5 this becomes a table of Addressable character prefabs; the ids stay the same.
    /// </summary>
    public static class EntityVisuals
    {
        public const uint PlayerDisplay = 1;
        public const uint FrogDisplay = 101;
        public const uint WolfDisplay = 102;
        public const uint BanditDisplay = 103;
        public const uint ShardcasterDisplay = 104;
        public const uint DummyDisplay = 105;
        public const uint CaptainDisplay = 110;
        public const uint HerbalistDisplay = 111;
        public const uint QuartermasterDisplay = 112;

        public static readonly Color LocalPlayerColor = new Color(0.25f, 0.55f, 1f);
        public static readonly Color PlayerColor = new Color(0.3f, 0.85f, 0.4f);
        public static readonly Color BotColor = new Color(1f, 0.6f, 0.2f);

        private static readonly Dictionary<Color, Material> LitMaterials = new Dictionary<Color, Material>();
        private static readonly Dictionary<Color, Material> GlowMaterials = new Dictionary<Color, Material>();
        private static Material _litTemplate;
        private static Material _unlitTemplate;

        /// <summary>Builds the visual for a display under <paramref name="parent"/> (no colliders).</summary>
        public static DisplayShape Create(Transform parent, uint displayId, Color playerColor)
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
                case BanditDisplay:
                {
                    Humanoid(parent, new Color(0.52f, 0.2f, 0.16f));
                    Part(PrimitiveType.Sphere, parent, new Vector3(0f, 1.78f, -0.05f), new Vector3(0.62f, 0.5f, 0.62f), new Color(0.18f, 0.14f, 0.12f));
                    Glow(parent, new Vector3(0f, 1.15f, 0.38f), new Vector3(0.12f, 0.2f, 0.06f), new Color(0.4f, 0.95f, 1f));
                    return new DisplayShape { Height = 2.1f, Radius = 0.5f, Hands = true };
                }
                case ShardcasterDisplay:
                {
                    Humanoid(parent, new Color(0.38f, 0.22f, 0.55f));
                    var crystal = Glow(parent, new Vector3(0f, 2.45f, 0f), new Vector3(0.22f, 0.36f, 0.22f), new Color(0.45f, 1f, 1f));
                    crystal.localRotation = Quaternion.Euler(0f, 45f, 0f);
                    crystal.gameObject.AddComponent<Spinner>();
                    return new DisplayShape { Height = 2.3f, Radius = 0.5f, Hands = true };
                }
                case CaptainDisplay:
                {
                    // Steel armor, a helmet and a blue cloak: the outpost's commander.
                    Humanoid(parent, new Color(0.46f, 0.52f, 0.62f));
                    Part(PrimitiveType.Sphere, parent, new Vector3(0f, 1.8f, 0f), new Vector3(0.62f, 0.42f, 0.62f), new Color(0.6f, 0.63f, 0.7f));
                    Part(PrimitiveType.Cube, parent, new Vector3(0f, 1.05f, -0.42f), new Vector3(0.72f, 1.35f, 0.06f), new Color(0.16f, 0.28f, 0.6f));
                    Part(PrimitiveType.Cube, parent, new Vector3(0f, 1.62f, 0f), new Vector3(0.95f, 0.14f, 0.5f), new Color(0.6f, 0.63f, 0.7f));
                    return new DisplayShape { Height = 2.1f, Radius = 0.5f, Hands = true };
                }
                case HerbalistDisplay:
                {
                    // A purple robe and a tall wide-brimmed hat.
                    Humanoid(parent, new Color(0.4f, 0.3f, 0.48f));
                    var hat = new Color(0.22f, 0.18f, 0.26f);
                    Part(PrimitiveType.Cylinder, parent, new Vector3(0f, 1.95f, 0f), new Vector3(0.95f, 0.025f, 0.95f), hat);
                    Part(PrimitiveType.Cylinder, parent, new Vector3(0f, 2.15f, 0f), new Vector3(0.45f, 0.2f, 0.45f), hat);
                    Part(PrimitiveType.Cylinder, parent, new Vector3(0.04f, 2.42f, 0f), new Vector3(0.26f, 0.1f, 0.26f), hat);
                    Part(PrimitiveType.Sphere, parent, new Vector3(0.08f, 2.56f, 0f), Vector3.one * 0.14f, hat);
                    return new DisplayShape { Height = 2.5f, Radius = 0.5f, Hands = true };
                }
                case QuartermasterDisplay:
                {
                    // Broad, in brown with a leather apron.
                    Part(PrimitiveType.Capsule, parent, new Vector3(0f, 1f, 0f), new Vector3(1.05f, 1f, 0.9f), new Color(0.48f, 0.34f, 0.22f));
                    Part(PrimitiveType.Cube, parent, new Vector3(0f, 1.55f, 0.42f), new Vector3(0.25f, 0.15f, 0.3f), new Color(0.3f, 0.2f, 0.12f));
                    Part(PrimitiveType.Cube, parent, new Vector3(0f, 0.85f, 0.43f), new Vector3(0.7f, 0.95f, 0.05f), new Color(0.72f, 0.6f, 0.42f));
                    return new DisplayShape { Height = 2.0f, Radius = 0.55f, Hands = true };
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
                    Humanoid(parent, displayId == PlayerDisplay ? playerColor : Color.gray);
                    return new DisplayShape { Height = 2.0f, Radius = 0.45f, Hands = true };
            }
        }

        /// <summary>The Phase 1 capsule with a "nose" that shows the facing.</summary>
        private static void Humanoid(Transform parent, Color color)
        {
            Part(PrimitiveType.Capsule, parent, new Vector3(0f, 1f, 0f), new Vector3(0.8f, 1f, 0.8f), color);
            Part(PrimitiveType.Cube, parent, new Vector3(0f, 1.55f, 0.38f), new Vector3(0.25f, 0.15f, 0.3f), color * 0.5f);
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
