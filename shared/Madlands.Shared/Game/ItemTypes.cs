using System.Collections.Generic;
using Madlands.Shared.Net;

namespace Madlands.Shared.Game
{
    // Items, inventory and money (Phase 4, ADR 0010). Enum values are stable: append, never renumber.

    public enum ItemQuality : byte
    {
        /// <summary>Gray: junk, only worth selling.</summary>
        Poor = 0,
        Common = 1,
        Uncommon = 2,
        Rare = 3,
        Epic = 4,
    }

    public enum ItemClass : byte
    {
        Junk = 0,
        Weapon = 1,
        Armor = 2,
        Consumable = 3,
        Container = 4,
        /// <summary>Only drops for players who need it for a quest; cannot be sold.</summary>
        Quest = 5,
        TradeGoods = 6,
    }

    /// <summary>Where an item can be equipped (None = it cannot).</summary>
    public enum InventoryType : byte
    {
        None = 0,
        Head = 1,
        Neck = 2,
        Shoulders = 3,
        Back = 4,
        Chest = 5,
        Wrists = 6,
        Hands = 7,
        Waist = 8,
        Legs = 9,
        Feet = 10,
        /// <summary>Either ring slot.</summary>
        Finger = 11,
        /// <summary>Either trinket slot.</summary>
        Trinket = 12,
        /// <summary>A one-handed weapon (main hand only: there is no dual wielding yet).</summary>
        MainHand = 13,
        /// <summary>A shield or held item: armor and stats, no attacks.</summary>
        OffHand = 14,
        /// <summary>A two-handed weapon: leaves the off hand empty.</summary>
        TwoHand = 15,
        /// <summary>A container that goes in one of the bag slots.</summary>
        Bag = 16,
    }

    /// <summary>The character's equipment slots (slots of <see cref="ItemPos.Equipment"/>).</summary>
    public enum EquipSlot : byte
    {
        Head = 0,
        Neck = 1,
        Shoulders = 2,
        Back = 3,
        Chest = 4,
        Wrists = 5,
        Hands = 6,
        Waist = 7,
        Legs = 8,
        Feet = 9,
        Finger1 = 10,
        Finger2 = 11,
        Trinket1 = 12,
        Trinket2 = 13,
        MainHand = 14,
        OffHand = 15,
        Bag1 = 16,
        Bag2 = 17,
        Bag3 = 18,
        Bag4 = 19,
    }

    /// <summary>Why an item, loot, vendor or quest action was refused. Shown as an error text.</summary>
    public enum ActionError : byte
    {
        None = 0,
        InventoryFull = 1,
        ItemNotFound = 2,
        CantEquipThere = 3,
        /// <summary>Value: the level needed.</summary>
        LevelTooLow = 4,
        WrongClass = 5,
        BagNotEmpty = 6,
        BagInItself = 7,
        NotABag = 8,
        NotEnoughMoney = 9,
        CantSellItem = 10,
        TooFarAway = 11,
        CantDoThatNow = 12,
        NotUsable = 13,
        TwoHandEquipped = 14,
        LootNotYours = 15,
        QuestLogFull = 16,
        QuestNotAvailable = 17,
        QuestNotComplete = 18,
        ChooseReward = 19,
        CantInteract = 20,
        /// <summary>The reward or bought items do not fit (no money or items were taken).</summary>
        NoRoomForReward = 21,
        CantDestroyItem = 22,
    }

    /// <summary>How an item or money reached the player (chat lines: "You receive loot", "Received ...").</summary>
    public enum ReceiveSource : byte
    {
        Loot = 0,
        Quest = 1,
        Vendor = 2,
        Other = 3,
    }

    [System.Flags]
    public enum NpcFlags : byte
    {
        None = 0,
        /// <summary>Has something to say when spoken to (right-click).</summary>
        Gossip = 1 << 0,
        QuestGiver = 1 << 1,
        Vendor = 1 << 2,
    }

    /// <summary>
    /// What an NPC is, for the player: the client shows its symbol over the head and on the maps, and a title
    /// under the name ("&lt;Innkeeper&gt;"). Values are stable: append, never renumber.
    /// </summary>
    public enum NpcRole : byte
    {
        None = 0,
        /// <summary>Sells supplies, bags and basic gear.</summary>
        GeneralGoods = 1,
        /// <summary>Runs an inn: food and drink.</summary>
        Innkeeper = 2,
        /// <summary>Sells weapons and shields.</summary>
        Weaponsmith = 3,
    }

    /// <summary>
    /// An inventory position: a container and a slot in it. Container 0 is the equipment (slot =
    /// <see cref="EquipSlot"/>), 1 the backpack, 2-5 the bags in the bag slots Bag1-Bag4.
    /// </summary>
    public struct ItemPos : System.IEquatable<ItemPos>
    {
        public const byte Equipment = 0;
        public const byte Backpack = 1;
        public const byte FirstBag = 2;
        public const byte ContainerCount = FirstBag + GameConstants.BagSlotCount;

        public byte Container;
        public byte Slot;

        public ItemPos(byte container, byte slot)
        {
            Container = container;
            Slot = slot;
        }

        public static ItemPos Equip(EquipSlot slot) => new ItemPos(Equipment, (byte)slot);

        public bool IsEquipment => Container == Equipment;
        public bool IsBagSlot => Container == Equipment && Slot >= (byte)EquipSlot.Bag1 && Slot <= (byte)EquipSlot.Bag4;
        /// <summary>For a bag container (2-5): the equipment slot its bag sits in.</summary>
        public static EquipSlot BagSlotOf(byte container) => (EquipSlot)((byte)EquipSlot.Bag1 + container - FirstBag);
        /// <summary>For a bag slot: the container number of the bag in it.</summary>
        public static byte ContainerOf(EquipSlot bagSlot) => (byte)(FirstBag + (bagSlot - EquipSlot.Bag1));

        public void Write(PacketWriter w)
        {
            w.WriteByte(Container);
            w.WriteByte(Slot);
        }

        public static ItemPos Read(ref PacketReader r) => new ItemPos(r.ReadByte(), r.ReadByte());

        public bool Equals(ItemPos other) => Container == other.Container && Slot == other.Slot;
        public override bool Equals(object obj) => obj is ItemPos other && Equals(other);
        public override int GetHashCode() => Container * 256 + Slot;
        public static bool operator ==(ItemPos a, ItemPos b) => a.Equals(b);
        public static bool operator !=(ItemPos a, ItemPos b) => !a.Equals(b);
        public override string ToString() => Container + ":" + Slot;
    }

    public struct ItemStat
    {
        public StatType Stat;
        public int Value;
    }

    /// <summary>
    /// What the client needs to show an item (bags, tooltips, vendor and loot windows). Sent in
    /// <see cref="GameData"/>; the rules that use these numbers run on the server.
    /// </summary>
    public sealed class ItemInfo
    {
        public const int MaxStats = 16;

        public uint Id;
        public string Name;
        public string Description;
        /// <summary>Client-side icon lookup id.</summary>
        public uint Icon;
        /// <summary>How it looks when equipped (weapons in hand, later armor on the body).</summary>
        public uint DisplayId;
        public ItemQuality Quality;
        public ItemClass Class;
        public InventoryType InventoryType;
        public byte RequiredLevel;
        /// <summary>Class ids that may use it; empty = everyone.</summary>
        public byte[] AllowedClasses = new byte[0];
        public ushort StackSize = 1;
        /// <summary>Copper a vendor charges for it.</summary>
        public uint BuyPrice;
        /// <summary>Copper a vendor pays for it; 0 = cannot be sold.</summary>
        public uint SellPrice;
        public readonly List<ItemStat> Stats = new List<ItemStat>();
        public int MinDamage;
        public int MaxDamage;
        /// <summary>Weapon speed; 0 for non-weapons.</summary>
        public int SpeedMs;
        /// <summary>Spell cast when the item is used (consumables); 0 = not usable.</summary>
        public uint UseSpell;
        /// <summary>Slots of a bag.</summary>
        public byte BagSlots;

        public bool IsWeapon => SpeedMs > 0;
        public bool IsEquippable => InventoryType != InventoryType.None;
        public float Dps => SpeedMs > 0 ? (MinDamage + MaxDamage) * 0.5f / (SpeedMs / 1000f) : 0f;

        public int GetStat(StatType stat)
        {
            int sum = 0;
            foreach (var s in Stats) if (s.Stat == stat) sum += s.Value;
            return sum;
        }

        public bool AllowsClass(byte classId)
        {
            if (AllowedClasses.Length == 0) return true;
            foreach (var c in AllowedClasses) if (c == classId) return true;
            return false;
        }

        public void Write(PacketWriter w)
        {
            w.WriteUInt(Id);
            w.WriteString(Name);
            w.WriteString(Description);
            w.WriteUInt(Icon);
            w.WriteUInt(DisplayId);
            w.WriteByte((byte)Quality);
            w.WriteByte((byte)Class);
            w.WriteByte((byte)InventoryType);
            w.WriteByte(RequiredLevel);
            w.WriteByte((byte)AllowedClasses.Length);
            foreach (var c in AllowedClasses) w.WriteByte(c);
            w.WriteUShort(StackSize);
            w.WriteUInt(BuyPrice);
            w.WriteUInt(SellPrice);
            w.WriteByte((byte)Stats.Count);
            foreach (var s in Stats)
            {
                w.WriteByte((byte)s.Stat);
                w.WriteInt(s.Value);
            }
            w.WriteInt(MinDamage);
            w.WriteInt(MaxDamage);
            w.WriteInt(SpeedMs);
            w.WriteUInt(UseSpell);
            w.WriteByte(BagSlots);
        }

        public static ItemInfo Read(ref PacketReader r)
        {
            var i = new ItemInfo();
            i.Id = r.ReadUInt();
            i.Name = r.ReadString(SpellInfo.MaxNameBytes);
            i.Description = r.ReadString(SpellInfo.MaxDescriptionBytes);
            i.Icon = r.ReadUInt();
            i.DisplayId = r.ReadUInt();
            i.Quality = (ItemQuality)r.ReadByte();
            i.Class = (ItemClass)r.ReadByte();
            i.InventoryType = (InventoryType)r.ReadByte();
            i.RequiredLevel = r.ReadByte();
            int classes = r.ReadByte();
            i.AllowedClasses = new byte[classes];
            for (int c = 0; c < classes; c++) i.AllowedClasses[c] = r.ReadByte();
            i.StackSize = r.ReadUShort();
            i.BuyPrice = r.ReadUInt();
            i.SellPrice = r.ReadUInt();
            int stats = r.ReadByte();
            if (stats > MaxStats) throw new PacketException("Item with " + stats + " stats.");
            for (int s = 0; s < stats; s++) i.Stats.Add(new ItemStat { Stat = (StatType)r.ReadByte(), Value = r.ReadInt() });
            i.MinDamage = r.ReadInt();
            i.MaxDamage = r.ReadInt();
            i.SpeedMs = r.ReadInt();
            i.UseSpell = r.ReadUInt();
            i.BagSlots = r.ReadByte();
            return i;
        }
    }

    /// <summary>Equipment rules both sides agree on (the client uses them to highlight slots and pick targets).</summary>
    public static class ItemRules
    {
        /// <summary>True if an item of this type may go into <paramref name="slot"/>.</summary>
        public static bool Fits(InventoryType type, EquipSlot slot)
        {
            switch (type)
            {
                case InventoryType.Head: return slot == EquipSlot.Head;
                case InventoryType.Neck: return slot == EquipSlot.Neck;
                case InventoryType.Shoulders: return slot == EquipSlot.Shoulders;
                case InventoryType.Back: return slot == EquipSlot.Back;
                case InventoryType.Chest: return slot == EquipSlot.Chest;
                case InventoryType.Wrists: return slot == EquipSlot.Wrists;
                case InventoryType.Hands: return slot == EquipSlot.Hands;
                case InventoryType.Waist: return slot == EquipSlot.Waist;
                case InventoryType.Legs: return slot == EquipSlot.Legs;
                case InventoryType.Feet: return slot == EquipSlot.Feet;
                case InventoryType.Finger: return slot == EquipSlot.Finger1 || slot == EquipSlot.Finger2;
                case InventoryType.Trinket: return slot == EquipSlot.Trinket1 || slot == EquipSlot.Trinket2;
                case InventoryType.MainHand:
                case InventoryType.TwoHand: return slot == EquipSlot.MainHand;
                case InventoryType.OffHand: return slot == EquipSlot.OffHand;
                case InventoryType.Bag: return slot >= EquipSlot.Bag1 && slot <= EquipSlot.Bag4;
                default: return false;
            }
        }

        /// <summary>The slots an item of this type can go into, best first.</summary>
        public static EquipSlot[] SlotsFor(InventoryType type)
        {
            switch (type)
            {
                case InventoryType.Finger: return new[] { EquipSlot.Finger1, EquipSlot.Finger2 };
                case InventoryType.Trinket: return new[] { EquipSlot.Trinket1, EquipSlot.Trinket2 };
                case InventoryType.Bag: return new[] { EquipSlot.Bag1, EquipSlot.Bag2, EquipSlot.Bag3, EquipSlot.Bag4 };
                case InventoryType.None: return new EquipSlot[0];
            }
            for (var slot = EquipSlot.Head; slot <= EquipSlot.OffHand; slot++)
                if (Fits(type, slot)) return new[] { slot };
            return new EquipSlot[0];
        }
    }

    /// <summary>Money is counted in copper: 100 copper = 1 silver, 100 silver = 1 gold.</summary>
    public static class Money
    {
        public const long CopperPerSilver = 100;
        public const long CopperPerGold = 10000;

        public static long Gold(long copper) => copper / CopperPerGold;
        public static long Silver(long copper) => copper % CopperPerGold / CopperPerSilver;
        public static long Copper(long copper) => copper % CopperPerSilver;

        /// <summary>"1g 5s 20c"; zero parts are left out ("0c" for nothing).</summary>
        public static string Format(long copper)
        {
            if (copper <= 0) return "0c";
            var parts = new List<string>(3);
            if (Gold(copper) > 0) parts.Add(Gold(copper) + "g");
            if (Silver(copper) > 0) parts.Add(Silver(copper) + "s");
            if (Copper(copper) > 0) parts.Add(Copper(copper) + "c");
            return string.Join(" ", parts);
        }
    }
}
