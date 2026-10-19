using System.Collections.Generic;
using Madlands.Shared.Game;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// Worn armor on humanoid models, by the items' display ids (content/items.json): body armor
    /// recolors the gear regions (robes also cover the legs), and helmets, hoods, hats, shoulder pads
    /// and cloaks are simple shapes sized from the race's own skeleton. Later these become modeled
    /// pieces; the display ids stay.
    /// </summary>
    public static class GearVisuals
    {
        private enum Style
        {
            Tint,
            Robe,
            Hood,
            Helm,
            WideHat,
            Pads,
            Mantle,
            Cloak,
        }

        private struct Look
        {
            public Style Style;
            public Color Color;

            public Look(Style style, string color)
            {
                Style = style;
                Color = EntityVisuals.Hex(color);
            }
        }

        private static readonly Dictionary<uint, Look> Looks = new Dictionary<uint, Look>
        {
            // Chest
            { 3001, new Look(Style.Tint, "#b5a27f") }, { 3002, new Look(Style.Robe, "#3f5a8a") }, { 3003, new Look(Style.Robe, "#d8cfa8") },
            { 3004, new Look(Style.Tint, "#6a5444") }, { 3005, new Look(Style.Robe, "#4a3a6a") }, { 3006, new Look(Style.Tint, "#4a7a6a") },
            { 3007, new Look(Style.Robe, "#5a6a7a") }, { 3008, new Look(Style.Tint, "#8a96a0") },
            // Legs
            { 3101, new Look(Style.Tint, "#5a4a38") }, { 3102, new Look(Style.Tint, "#6a5a40") }, { 3103, new Look(Style.Tint, "#4a6a7a") },
            { 3104, new Look(Style.Tint, "#3d5a35") },
            // Feet
            { 3201, new Look(Style.Tint, "#4a3424") }, { 3202, new Look(Style.Tint, "#5b7a3a") }, { 3203, new Look(Style.Tint, "#6a4a2a") },
            // Hands
            { 3301, new Look(Style.Tint, "#c8b894") }, { 3302, new Look(Style.Tint, "#4a6a3a") }, { 3303, new Look(Style.Tint, "#7a6a5a") },
            { 3304, new Look(Style.Tint, "#6a6a7a") }, { 3305, new Look(Style.Tint, "#6a7a8a") },
            // Waist
            { 3401, new Look(Style.Tint, "#3a2a1a") }, { 3402, new Look(Style.Tint, "#c8a840") }, { 3403, new Look(Style.Tint, "#8a6a4a") },
            // Head
            { 3501, new Look(Style.Hood, "#3a3a3a") }, { 3502, new Look(Style.WideHat, "#d8bf6a") }, { 3503, new Look(Style.Helm, "#8a96a0") },
            { 3504, new Look(Style.Hood, "#ddd7c3") },
            // Shoulders
            { 3601, new Look(Style.Pads, "#6a5a4a") }, { 3602, new Look(Style.Mantle, "#4a3a2c") }, { 3603, new Look(Style.Pads, "#5a6a8a") },
            { 3604, new Look(Style.Pads, "#4a4146") },
            // Back
            { 3701, new Look(Style.Cloak, "#4a5a35") },
        };

        private static readonly string[] TintedRegions = { "Chest", "Arms", "Hands", "Waist", "Legs", "Feet" };

        private static int SlotOf(EquipSlot slot) => System.Array.IndexOf(VisibleGear.Slots, slot);

        /// <summary>
        /// Dresses a humanoid in <paramref name="gear"/> (VisibleGear order) from scratch: regions back to
        /// the model's colors, old shapes removed, then every worn piece applied.
        /// </summary>
        public static void Apply(DisplayModel model, uint[] gear, List<GameObject> parts)
        {
            foreach (var part in parts)
                if (part != null) Object.Destroy(part);
            parts.Clear();
            foreach (var region in TintedRegions) model.ResetTint(region);

            // Legs before the chest: a robe covers them.
            Tint(model, gear[SlotOf(EquipSlot.Legs)], "Legs");
            Tint(model, gear[SlotOf(EquipSlot.Feet)], "Feet");
            Tint(model, gear[SlotOf(EquipSlot.Hands)], "Hands");
            Tint(model, gear[SlotOf(EquipSlot.Waist)], "Waist");
            if (Looks.TryGetValue(gear[SlotOf(EquipSlot.Chest)], out var chest))
            {
                model.Tint("Chest", chest.Color);
                model.Tint("Arms", chest.Color * 0.92f);
                if (chest.Style == Style.Robe)
                {
                    model.Tint("Legs", chest.Color * 0.9f);
                    Skirt(model, chest.Color, parts);
                }
            }
            foreach (var slot in new[] { EquipSlot.Head, EquipSlot.Shoulders, EquipSlot.Back })
                if (Looks.TryGetValue(gear[SlotOf(slot)], out var look)) Shape(model, look, parts);
        }

        private static void Tint(DisplayModel model, uint display, string region)
        {
            if (Looks.TryGetValue(display, out var look)) model.Tint(region, look.Color);
        }

        /// <summary>The robe's skirt, from the waist to the knees.</summary>
        private static void Skirt(DisplayModel model, Color color, List<GameObject> parts)
        {
            float hips = model.RestPosition("Hips").y;
            float knee = model.RestPosition("LeftLowerLeg").y;
            float width = Mathf.Abs(model.RestPosition("LeftUpperLeg").x) * 3.3f;
            float top = hips + 0.08f, bottom = knee - 0.05f;
            parts.Add(model.Attach("Hips", PrimitiveType.Cylinder, new Vector3(0f, (top + bottom) / 2f, 0f),
                new Vector3(width, (top - bottom) / 2f, width * 0.8f), color * 0.9f).gameObject);
        }

        private static void Shape(DisplayModel model, Look look, List<GameObject> parts)
        {
            var head = model.RestPosition("Head");
            float top = model.Height;
            float size = (top - head.y) / 0.29f;
            float center = (head.y + top) / 2f + 0.01f;
            var shoulder = model.RestPosition("LeftUpperArm");
            float wide = Mathf.Abs(shoulder.x) / 0.19f;
            var dark = look.Color * 0.75f;
            dark.a = 1f;
            switch (look.Style)
            {
                case Style.Hood:
                    parts.Add(model.Attach("Head", PrimitiveType.Sphere, new Vector3(0f, center + 0.02f * size, -0.02f), new Vector3(0.27f, 0.27f, 0.31f) * size, look.Color).gameObject);
                    parts.Add(model.Attach("Chest", PrimitiveType.Cube, new Vector3(0f, shoulder.y + 0.02f, -0.1f * wide), new Vector3(0.24f * wide, 0.18f, 0.06f), look.Color).gameObject);
                    break;
                case Style.Helm:
                    parts.Add(model.Attach("Head", PrimitiveType.Sphere, new Vector3(0f, center + 0.05f * size, 0f), new Vector3(0.27f, 0.21f, 0.29f) * size, look.Color).gameObject);
                    parts.Add(model.Attach("Head", PrimitiveType.Cube, new Vector3(0f, center - 0.02f * size, 0f), new Vector3(0.29f, 0.03f, 0.31f) * size, dark).gameObject);
                    parts.Add(model.Attach("Head", PrimitiveType.Cube, new Vector3(0f, center - 0.02f * size, 0.14f * size), new Vector3(0.03f, 0.14f, 0.02f) * size, dark).gameObject);
                    break;
                case Style.WideHat:
                    parts.Add(model.Attach("Head", PrimitiveType.Cylinder, new Vector3(0f, top - 0.05f * size, 0f), new Vector3(0.52f, 0.012f, 0.52f) * size, look.Color).gameObject);
                    parts.Add(model.Attach("Head", PrimitiveType.Cylinder, new Vector3(0f, top + 0.01f * size, 0f), new Vector3(0.25f, 0.07f, 0.25f) * size, look.Color).gameObject);
                    break;
                case Style.Pads:
                case Style.Mantle:
                {
                    float scale = look.Style == Style.Mantle ? 1.3f : 1f;
                    foreach (float side in new[] { -1f, 1f })
                        parts.Add(model.Attach("Chest", PrimitiveType.Sphere, new Vector3(side * Mathf.Abs(shoulder.x) * 1.05f, shoulder.y + 0.04f, 0f),
                            new Vector3(0.2f, 0.12f, 0.22f) * wide * scale, look.Color).gameObject);
                    if (look.Style == Style.Mantle)
                        parts.Add(model.Attach("Chest", PrimitiveType.Cylinder, new Vector3(0f, shoulder.y + 0.05f, 0f), new Vector3(0.36f * wide, 0.05f, 0.28f * wide), dark).gameObject);
                    break;
                }
                case Style.Cloak:
                {
                    float hips = model.RestPosition("Hips").y;
                    float bottom = hips * 0.45f;
                    float height = shoulder.y + 0.02f - bottom;
                    parts.Add(model.Attach("Chest", PrimitiveType.Cube, new Vector3(0f, bottom + height / 2f, -0.14f * wide),
                        new Vector3(0.46f * wide, height, 0.03f), look.Color, new Vector3(-5f, 0f, 0f)).gameObject);
                    break;
                }
            }
        }
    }
}
