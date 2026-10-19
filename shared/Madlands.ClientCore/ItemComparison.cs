using System;
using System.Collections.Generic;
using Madlands.Shared.Game;

namespace Madlands.ClientCore
{
    /// <summary>One line of an item comparison: how a number changes if the item is equipped.</summary>
    public struct StatChange
    {
        /// <summary>The stat; null for weapon damage per second.</summary>
        public StatType? Stat;
        public float Delta;
    }

    /// <summary>
    /// What equipping an item would do, for the "currently equipped" comparison in tooltips: which equipped
    /// items it would replace (the same choice the server makes) and how the numbers change.
    /// </summary>
    public static class ItemComparison
    {
        /// <summary>Armor first, then the stats in their usual order.</summary>
        private static readonly StatType[] Order =
        {
            StatType.Armor, StatType.Strength, StatType.Agility, StatType.Stamina, StatType.Intellect, StatType.Spirit,
            StatType.AttackPower, StatType.SpellPower,
        };

        public static List<ItemInfo> Replaced(ItemInfo item, ClientInventory inventory, ClientGameData data) =>
            Replaced(item, slot => data.GetItem(inventory.Equipped(slot).ItemId));

        /// <param name="equipped">The item in a slot, or null.</param>
        public static List<ItemInfo> Replaced(ItemInfo item, Func<EquipSlot, ItemInfo> equipped)
        {
            var replaced = new List<ItemInfo>(2);
            if (item == null || !item.IsEquippable || item.InventoryType == InventoryType.Bag) return replaced;
            var mainHand = equipped(EquipSlot.MainHand);
            switch (item.InventoryType)
            {
                case InventoryType.TwoHand:
                    // Takes both hands.
                    Add(replaced, mainHand);
                    Add(replaced, equipped(EquipSlot.OffHand));
                    return replaced;
                case InventoryType.OffHand:
                    // Next to a two-handed weapon, that weapon goes.
                    Add(replaced, mainHand != null && mainHand.InventoryType == InventoryType.TwoHand ? mainHand : equipped(EquipSlot.OffHand));
                    return replaced;
            }
            // Rings and trinkets go into a free slot first: nothing is replaced then.
            var slots = ItemRules.SlotsFor(item.InventoryType);
            foreach (var slot in slots)
                if (equipped(slot) == null) return replaced;
            if (slots.Length > 0) Add(replaced, equipped(slots[0]));
            return replaced;
        }

        /// <summary>What changes if <paramref name="item"/> takes the place of <paramref name="replaced"/>; unchanged numbers are left out.</summary>
        public static List<StatChange> Changes(ItemInfo item, IReadOnlyList<ItemInfo> replaced)
        {
            var changes = new List<StatChange>();
            bool weapons = item.IsWeapon;
            float oldDps = 0f;
            foreach (var r in replaced)
            {
                if (!r.IsWeapon) continue;
                weapons = true;
                oldDps += r.Dps;
            }
            float dps = item.Dps - oldDps;
            if (weapons && Math.Abs(dps) >= 0.05f) changes.Add(new StatChange { Stat = null, Delta = dps });
            foreach (var stat in Order)
            {
                int delta = item.GetStat(stat);
                foreach (var r in replaced) delta -= r.GetStat(stat);
                if (delta != 0) changes.Add(new StatChange { Stat = stat, Delta = delta });
            }
            return changes;
        }

        private static void Add(List<ItemInfo> list, ItemInfo item)
        {
            if (item != null) list.Add(item);
        }
    }
}
