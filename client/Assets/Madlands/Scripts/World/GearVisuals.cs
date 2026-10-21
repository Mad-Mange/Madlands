using System.Collections.Generic;
using Madlands.Shared.Game;
using UnityEngine;

namespace Madlands.Client
{
    /// <summary>
    /// Worn armor on humanoid models, by the items' display ids (content/items.json): body armor
    /// recolors the gear regions, and the rest shows modeled pieces the race's model carries
    /// (art/blender/gear.py) in the item's color: robes, breastplates, helmets, hoods and hats, shoulder
    /// pads and mantles, cloaks.
    /// </summary>
    public static class GearVisuals
    {
        private struct Look
        {
            /// <summary>The color of the item's gear region (and of its pieces, unless they have their own).</summary>
            public Color Color;
            public Color PieceColor;
            /// <summary>Gear pieces shown (none: only the region is tinted).</summary>
            public string[] Pieces;

            public Look(string color, params string[] pieces)
            {
                Color = PieceColor = EntityVisuals.Hex(color);
                Pieces = pieces;
            }

            /// <summary>Pieces in a color of their own, like steel greaves over green trousers.</summary>
            public static Look Plated(string color, string pieceColor, params string[] pieces) =>
                new Look(color, pieces) { PieceColor = EntityVisuals.Hex(pieceColor) };
        }

        private static readonly string[] Robe = { "RobeSkirt", "RobeCuffs" };

        private static readonly Dictionary<uint, Look> Looks = new Dictionary<uint, Look>
        {
            // Chest
            { 3001, new Look("#b5a27f") }, { 3002, new Look("#3f5a8a", Robe) }, { 3003, new Look("#d8cfa8", Robe) },
            { 3004, new Look("#6a5444") }, { 3005, new Look("#4a3a6a", Robe) }, { 3006, new Look("#4a7a6a") },
            { 3007, new Look("#5a6a7a", Robe) }, { 3008, new Look("#8a96a0", "Breastplate") },
            // Legs
            { 3101, new Look("#5a4a38") }, { 3102, new Look("#6a5a40") }, { 3103, new Look("#4a6a7a") },
            { 3104, Look.Plated("#3d5a35", "#8a9098", "Greaves") },
            // Feet
            { 3201, new Look("#4a3424") }, { 3202, new Look("#5b7a3a") }, { 3203, new Look("#6a4a2a") },
            // Hands
            { 3301, new Look("#c8b894") }, { 3302, new Look("#4a6a3a") }, { 3303, new Look("#7a6a5a") }, { 3304, new Look("#6a6a7a") },
            { 3305, new Look("#6a7a8a", "Gauntlets") },
            // Waist
            { 3401, new Look("#3a2a1a") }, { 3402, new Look("#c8a840", "Sash") }, { 3403, new Look("#8a6a4a") },
            // Head
            { 3501, new Look("#3a3a3a", "Hood") }, { 3502, new Look("#d8bf6a", "StrawHat") }, { 3503, new Look("#8a96a0", "Helm") },
            { 3504, new Look("#3d3546", "BoneHood") },
            // Shoulders
            { 3601, new Look("#6a5a4a", "LeatherPads") }, { 3602, new Look("#4a3a2c", "FurMantle") }, { 3603, new Look("#5a6a8a", "Pauldrons") },
            { 3604, new Look("#4a4146", "SpikedPauldrons") },
            // Back
            { 3701, new Look("#4a5a35", "Cloak") },
        };

        /// <summary>Head pieces the hair would poke through.</summary>
        private static readonly HashSet<string> HidesHair = new HashSet<string> { "Hood", "BoneHood", "Helm", "HornedHelm", "MinerHelm", "OilskinHat" };

        private static readonly string[] TintedRegions = { "Chest", "Arms", "Hands", "Waist", "Legs", "Feet" };

        /// <summary>Slots whose item colors one gear region of the body.</summary>
        private static readonly (EquipSlot Slot, string Region)[] RegionSlots =
        {
            (EquipSlot.Legs, "Legs"), (EquipSlot.Feet, "Feet"), (EquipSlot.Hands, "Hands"), (EquipSlot.Waist, "Waist"),
        };

        private static int SlotOf(EquipSlot slot) => System.Array.IndexOf(VisibleGear.Slots, slot);

        /// <summary>
        /// Dresses a humanoid in <paramref name="gear"/> (VisibleGear order) from scratch: regions back to
        /// the model's colors, pieces taken off, then every worn item applied.
        /// </summary>
        public static void Apply(DisplayModel model, uint[] gear)
        {
            model.HidePieces();
            foreach (var region in TintedRegions) model.ResetTint(region);
            model.SetHidden("Hair", false);
            model.SetHidden("Legs", false);

            foreach (var (slot, region) in RegionSlots)
            {
                if (!Looks.TryGetValue(gear[SlotOf(slot)], out var look)) continue;
                model.Tint(region, look.Color);
                foreach (var piece in look.Pieces) Wear(model, piece, look.PieceColor);
            }
            if (Looks.TryGetValue(gear[SlotOf(EquipSlot.Chest)], out var chest))
            {
                // Under a breastplate the shirt is a darker padding.
                var cloth = chest.Pieces.Length > 0 && chest.Pieces[0] == "Breastplate" ? chest.Color * 0.6f : chest.Color;
                model.Tint("Chest", cloth);
                model.Tint("Arms", cloth * 0.92f);
                foreach (var piece in chest.Pieces) Wear(model, piece, chest.PieceColor);
            }
            foreach (var slot in new[] { EquipSlot.Head, EquipSlot.Shoulders, EquipSlot.Back })
                if (Looks.TryGetValue(gear[SlotOf(slot)], out var look))
                    foreach (var piece in look.Pieces) Wear(model, piece, look.PieceColor);
        }

        /// <summary>
        /// Shows a gear piece and hides what it covers: the hair under helmets and hoods, the hips and thighs
        /// under a robe's skirt (it moves with them; the shins stay).
        /// </summary>
        public static void Wear(DisplayModel model, string piece, Color color)
        {
            if (!model.ShowPiece(piece, color))
            {
                Debug.LogWarning($"[GearVisuals] {model.Root.name} has no gear piece {piece}");
                return;
            }
            if (HidesHair.Contains(piece)) model.SetHidden("Hair", true);
            if (piece == "RobeSkirt" || piece == "Rags") model.SetHidden("Legs", true);
        }
    }
}
