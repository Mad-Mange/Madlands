using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// Art for held items by the item's displayId (like units: the server never names assets): the Blender
    /// models, or primitives if a model is missing. Built under a hand anchor whose origin is the grip;
    /// +Y points along the weapon.
    /// </summary>
    public static class ItemVisuals
    {
        public const uint Shortsword = 2001;
        public const uint Staff = 2002;
        public const uint Cudgel = 2003;
        public const uint Hatchet = 2004;
        public const uint Cutlass = 2005;
        public const uint Buckler = 2006;
        public const uint CrystalRod = 2007;
        public const uint OrnateMace = 2008;
        public const uint GreatAxe = 2009;
        public const uint Cleaver = 2010;

        private static readonly Color Wood = new Color(0.42f, 0.29f, 0.17f);
        private static readonly Color DarkWood = new Color(0.3f, 0.2f, 0.12f);
        private static readonly Color Steel = new Color(0.78f, 0.8f, 0.84f);
        private static readonly Color DarkSteel = new Color(0.4f, 0.42f, 0.46f);
        private static readonly Color Gold = new Color(0.86f, 0.68f, 0.25f);

        /// <summary>Degrees a shield turns about its grip (the anchor's +Y), see Create.</summary>
        private const float ShieldTurn = 90f;

        /// <summary>Held in both hands: swings with the two-handed attack.</summary>
        public static bool IsTwoHanded(uint displayId) => displayId == Staff || displayId == CrystalRod || displayId == GreatAxe || displayId == Cleaver;

        /// <summary>A staff: stands upright in the hand while the holder is idle or moving (UnitView).</summary>
        public static bool IsStaff(uint displayId) => displayId == Staff || displayId == CrystalRod;

        /// <summary>The Blender model of each display (art/blender/items.py).</summary>
        private static readonly System.Collections.Generic.Dictionary<uint, string> Models = new System.Collections.Generic.Dictionary<uint, string>
        {
            { Shortsword, "Shortsword" }, { Staff, "Staff" }, { Cudgel, "Cudgel" }, { Hatchet, "Hatchet" }, { Cutlass, "Cutlass" },
            { Buckler, "Buckler" }, { CrystalRod, "CrystalRod" }, { OrnateMace, "OrnateMace" }, { GreatAxe, "GreatAxe" },
            { Cleaver, "Cleaver" },
        };

        public static void Create(Transform hand, uint displayId)
        {
            var catalog = ArtCatalog.Instance;
            var prefab = catalog != null && Models.TryGetValue(displayId, out var name) ? catalog.GetModel(name) : null;
            if (prefab != null)
            {
                var item = Object.Instantiate(prefab, hand, false).transform;
                // A shield's face looks along the arm in the T-pose, so a hanging arm held it flat like a tray:
                // turned about the grip, it faces out from the forearm.
                if (displayId == Buckler) item.localRotation = Quaternion.AngleAxis(ShieldTurn, Vector3.up) * item.localRotation;
                return;
            }
            switch (displayId)
            {
                case Shortsword:
                    Grip(hand, Wood);
                    EntityVisuals.Part(PrimitiveType.Cube, hand, new Vector3(0f, 0.09f, 0f), new Vector3(0.22f, 0.035f, 0.05f), DarkSteel);
                    EntityVisuals.Part(PrimitiveType.Cube, hand, new Vector3(0f, 0.42f, 0f), new Vector3(0.06f, 0.6f, 0.015f), Steel);
                    break;
                case Cutlass:
                    Grip(hand, DarkWood);
                    EntityVisuals.Part(PrimitiveType.Cube, hand, new Vector3(0f, 0.09f, 0f), new Vector3(0.2f, 0.04f, 0.06f), Gold);
                    var blade = EntityVisuals.Part(PrimitiveType.Cube, hand, new Vector3(0.03f, 0.4f, 0f), new Vector3(0.08f, 0.56f, 0.015f), Steel);
                    blade.localRotation = Quaternion.Euler(0f, 0f, -8f);
                    break;
                case Staff:
                    EntityVisuals.Part(PrimitiveType.Cylinder, hand, new Vector3(0f, 0.35f, 0f), new Vector3(0.05f, 0.9f, 0.05f), Wood);
                    EntityVisuals.Part(PrimitiveType.Sphere, hand, new Vector3(0f, 1.27f, 0f), Vector3.one * 0.12f, DarkWood);
                    break;
                case CrystalRod:
                    EntityVisuals.Part(PrimitiveType.Cylinder, hand, new Vector3(0f, 0.35f, 0f), new Vector3(0.045f, 0.85f, 0.045f), DarkSteel);
                    EntityVisuals.Part(PrimitiveType.Cylinder, hand, new Vector3(0f, 1.18f, 0f), new Vector3(0.12f, 0.03f, 0.12f), Steel);
                    var crystal = EntityVisuals.Glow(hand, new Vector3(0f, 1.36f, 0f), new Vector3(0.12f, 0.24f, 0.12f), new Color(0.45f, 1f, 1f));
                    crystal.localRotation = Quaternion.Euler(0f, 45f, 0f);
                    crystal.gameObject.AddComponent<Spinner>();
                    break;
                case Cudgel:
                    EntityVisuals.Part(PrimitiveType.Cylinder, hand, new Vector3(0f, 0.12f, 0f), new Vector3(0.04f, 0.18f, 0.04f), Wood);
                    EntityVisuals.Part(PrimitiveType.Cylinder, hand, new Vector3(0f, 0.42f, 0f), new Vector3(0.1f, 0.14f, 0.1f), DarkWood);
                    break;
                case OrnateMace:
                    EntityVisuals.Part(PrimitiveType.Cylinder, hand, new Vector3(0f, 0.14f, 0f), new Vector3(0.04f, 0.2f, 0.04f), Gold);
                    EntityVisuals.Part(PrimitiveType.Sphere, hand, new Vector3(0f, 0.44f, 0f), Vector3.one * 0.18f, Steel);
                    foreach (var dir in new[] { Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
                        EntityVisuals.Part(PrimitiveType.Cube, hand, new Vector3(0f, 0.44f, 0f) + dir * 0.1f, Vector3.one * 0.06f, Gold);
                    break;
                case Hatchet:
                    EntityVisuals.Part(PrimitiveType.Cylinder, hand, new Vector3(0f, 0.2f, 0f), new Vector3(0.035f, 0.28f, 0.035f), Wood);
                    EntityVisuals.Part(PrimitiveType.Cube, hand, new Vector3(0.07f, 0.42f, 0f), new Vector3(0.18f, 0.12f, 0.03f), Steel);
                    break;
                case GreatAxe:
                    EntityVisuals.Part(PrimitiveType.Cylinder, hand, new Vector3(0f, 0.35f, 0f), new Vector3(0.05f, 0.75f, 0.05f), Wood);
                    EntityVisuals.Part(PrimitiveType.Cube, hand, new Vector3(0.16f, 0.95f, 0f), new Vector3(0.38f, 0.32f, 0.04f), Steel);
                    EntityVisuals.Part(PrimitiveType.Cube, hand, new Vector3(-0.08f, 0.95f, 0f), new Vector3(0.12f, 0.08f, 0.04f), DarkSteel);
                    break;
                case Buckler:
                    var shield = EntityVisuals.Part(PrimitiveType.Cylinder, hand, new Vector3(-0.04f, 0.1f, 0f), new Vector3(0.55f, 0.025f, 0.55f), Wood);
                    shield.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    EntityVisuals.Part(PrimitiveType.Sphere, hand, new Vector3(-0.08f, 0.1f, 0f), Vector3.one * 0.12f, Steel);
                    break;
                default:
                    // Unknown display: a plain stick, so it is visible that something is held.
                    EntityVisuals.Part(PrimitiveType.Cylinder, hand, new Vector3(0f, 0.3f, 0f), new Vector3(0.05f, 0.35f, 0.05f), Wood);
                    break;
            }
        }

        private static void Grip(Transform hand, Color color) =>
            EntityVisuals.Part(PrimitiveType.Cylinder, hand, Vector3.zero, new Vector3(0.035f, 0.08f, 0.035f), color);
    }
}
