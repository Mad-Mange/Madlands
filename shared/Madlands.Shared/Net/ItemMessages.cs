using System.Collections.Generic;
using Madlands.Shared.Game;

namespace Madlands.Shared.Net
{
    // Phase 4: inventory, equipment, money, loot and vendors (ADR 0010).
    // Every message has a roundtrip test in Madlands.Server.Tests/ProtocolTests.cs.

    // ---------------------------------------------------------------- Client -> Server

    /// <summary>
    /// Moves an item: into an empty slot, swapping with the item there, or onto a stack of the same
    /// item. Moving into or out of the equipment container equips or unequips. <see cref="Count"/>
    /// splits a stack (0 = the whole stack).
    /// </summary>
    public sealed class MoveItem : IMessage
    {
        public ItemPos From;
        public ItemPos To;
        public ushort Count;

        public Opcode Opcode => Opcode.MoveItem;

        public void Write(PacketWriter w)
        {
            From.Write(w);
            To.Write(w);
            w.WriteUShort(Count);
        }

        public void Read(ref PacketReader r)
        {
            From = ItemPos.Read(ref r);
            To = ItemPos.Read(ref r);
            Count = r.ReadUShort();
        }
    }

    /// <summary>Uses an item (casts its spell, taking one from the stack). Replies are those of a cast with <see cref="CastId"/>.</summary>
    public sealed class UseItem : IMessage
    {
        public ItemPos Pos;
        public EntityGuid Target;
        public byte CastId;

        public Opcode Opcode => Opcode.UseItem;

        public void Write(PacketWriter w)
        {
            Pos.Write(w);
            w.WriteGuid(Target);
            w.WriteByte(CastId);
        }

        public void Read(ref PacketReader r)
        {
            Pos = ItemPos.Read(ref r);
            Target = r.ReadGuid();
            CastId = r.ReadByte();
        }
    }

    /// <summary>Destroys the whole stack at a position.</summary>
    public sealed class DestroyItem : IMessage
    {
        public ItemPos Pos;

        public Opcode Opcode => Opcode.DestroyItem;
        public void Write(PacketWriter w) => Pos.Write(w);
        public void Read(ref PacketReader r) => Pos = ItemPos.Read(ref r);
    }

    /// <summary>
    /// Right-click on an item: a bag item is equipped in the slot it fits (swapping out what was
    /// there); an equipped item goes into the first free bag slot.
    /// </summary>
    public sealed class AutoEquip : IMessage
    {
        public ItemPos Pos;

        public Opcode Opcode => Opcode.AutoEquip;
        public void Write(PacketWriter w) => Pos.Write(w);
        public void Read(ref PacketReader r) => Pos = ItemPos.Read(ref r);
    }

    public enum InteractOption : byte
    {
        /// <summary>Right-click: the NPC's dialog (or its wares, if selling is all it does).</summary>
        Talk = 0,
        /// <summary>"Let me browse your goods" in the dialog.</summary>
        Vendor = 1,
    }

    /// <summary>Speaks with an NPC. The server answers with a <see cref="GossipMenu"/> or a <see cref="VendorInventory"/>.</summary>
    public sealed class Interact : IMessage
    {
        public EntityGuid Npc;
        public InteractOption Option;

        public Opcode Opcode => Opcode.Interact;

        public void Write(PacketWriter w)
        {
            w.WriteGuid(Npc);
            w.WriteByte((byte)Option);
        }

        public void Read(ref PacketReader r)
        {
            Npc = r.ReadGuid();
            Option = (InteractOption)r.ReadByte();
        }
    }

    /// <summary>Opens the loot of a corpse (answered with a <see cref="LootWindow"/>).</summary>
    public sealed class LootOpen : IMessage
    {
        public EntityGuid Corpse;

        public Opcode Opcode => Opcode.LootOpen;
        public void Write(PacketWriter w) => w.WriteGuid(Corpse);
        public void Read(ref PacketReader r) => Corpse = r.ReadGuid();
    }

    /// <summary>Takes one entry of the open loot window: an item slot, the money, or everything.</summary>
    public sealed class LootTake : IMessage
    {
        public const byte MoneySlot = 254;
        public const byte AllSlots = 255;

        public byte Slot;

        public Opcode Opcode => Opcode.LootTake;
        public void Write(PacketWriter w) => w.WriteByte(Slot);
        public void Read(ref PacketReader r) => Slot = r.ReadByte();
    }

    public sealed class LootClose : IMessage
    {
        public Opcode Opcode => Opcode.LootClose;
        public void Write(PacketWriter w) { }
        public void Read(ref PacketReader r) { }
    }

    public sealed class VendorBuy : IMessage
    {
        public EntityGuid Vendor;
        public uint ItemId;
        public ushort Count;

        public Opcode Opcode => Opcode.VendorBuy;

        public void Write(PacketWriter w)
        {
            w.WriteGuid(Vendor);
            w.WriteUInt(ItemId);
            w.WriteUShort(Count);
        }

        public void Read(ref PacketReader r)
        {
            Vendor = r.ReadGuid();
            ItemId = r.ReadUInt();
            Count = r.ReadUShort();
        }
    }

    /// <summary>Sells the whole stack at a position.</summary>
    public sealed class VendorSell : IMessage
    {
        public EntityGuid Vendor;
        public ItemPos Pos;

        public Opcode Opcode => Opcode.VendorSell;

        public void Write(PacketWriter w)
        {
            w.WriteGuid(Vendor);
            Pos.Write(w);
        }

        public void Read(ref PacketReader r)
        {
            Vendor = r.ReadGuid();
            Pos = ItemPos.Read(ref r);
        }
    }

    // ---------------------------------------------------------------- Server -> Client

    public struct InventoryEntry
    {
        public ItemPos Pos;
        /// <summary>0 = the slot is empty now.</summary>
        public uint ItemId;
        public ushort Count;
    }

    /// <summary>Changed inventory slots. With <see cref="Full"/> the list replaces everything (login).</summary>
    public sealed class InventoryUpdate : IMessage
    {
        public const int MaxEntries = 512;

        public bool Full;
        public readonly List<InventoryEntry> Entries = new List<InventoryEntry>();

        public Opcode Opcode => Opcode.InventoryUpdate;

        public void Write(PacketWriter w)
        {
            w.WriteBool(Full);
            w.WriteUShort((ushort)Entries.Count);
            foreach (var e in Entries)
            {
                e.Pos.Write(w);
                w.WriteUInt(e.ItemId);
                w.WriteUShort(e.Count);
            }
        }

        public void Read(ref PacketReader r)
        {
            Entries.Clear();
            Full = r.ReadBool();
            int count = r.ReadUShort();
            if (count > MaxEntries) throw new PacketException("Too many inventory entries: " + count + ".");
            for (int i = 0; i < count; i++)
            {
                InventoryEntry e;
                e.Pos = ItemPos.Read(ref r);
                e.ItemId = r.ReadUInt();
                e.Count = r.ReadUShort();
                Entries.Add(e);
            }
        }
    }

    /// <summary>The player's money changed (or is announced at login with Delta 0).</summary>
    public sealed class MoneyUpdate : IMessage
    {
        public long Money;
        public long Delta;
        public ReceiveSource Source;

        public Opcode Opcode => Opcode.MoneyUpdate;

        public void Write(PacketWriter w)
        {
            w.WriteLong(Money);
            w.WriteLong(Delta);
            w.WriteByte((byte)Source);
        }

        public void Read(ref PacketReader r)
        {
            Money = r.ReadLong();
            Delta = r.ReadLong();
            Source = (ReceiveSource)r.ReadByte();
        }
    }

    /// <summary>Items were added to the bags (chat: "You receive loot: ...").</summary>
    public sealed class ItemReceived : IMessage
    {
        public uint ItemId;
        public ushort Count;
        public ReceiveSource Source;

        public Opcode Opcode => Opcode.ItemReceived;

        public void Write(PacketWriter w)
        {
            w.WriteUInt(ItemId);
            w.WriteUShort(Count);
            w.WriteByte((byte)Source);
        }

        public void Read(ref PacketReader r)
        {
            ItemId = r.ReadUInt();
            Count = r.ReadUShort();
            Source = (ReceiveSource)r.ReadByte();
        }
    }

    /// <summary>An item, loot, vendor or quest request was refused.</summary>
    public sealed class ActionFailed : IMessage
    {
        public ActionError Error;
        /// <summary>Detail for some errors (the level needed for LevelTooLow).</summary>
        public int Value;

        public Opcode Opcode => Opcode.ActionFailed;

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)Error);
            w.WriteInt(Value);
        }

        public void Read(ref PacketReader r)
        {
            Error = (ActionError)r.ReadByte();
            Value = r.ReadInt();
        }
    }

    public struct LootEntry
    {
        public byte Slot;
        public uint ItemId;
        public ushort Count;
    }

    /// <summary>The contents of the loot window (resent when they change). Open = false closes it.</summary>
    public sealed class LootWindow : IMessage
    {
        public const int MaxEntries = 32;

        public EntityGuid Corpse;
        public bool Open;
        public long Money;
        public readonly List<LootEntry> Entries = new List<LootEntry>();

        public Opcode Opcode => Opcode.LootWindow;

        public void Write(PacketWriter w)
        {
            w.WriteGuid(Corpse);
            w.WriteBool(Open);
            w.WriteLong(Money);
            w.WriteByte((byte)Entries.Count);
            foreach (var e in Entries)
            {
                w.WriteByte(e.Slot);
                w.WriteUInt(e.ItemId);
                w.WriteUShort(e.Count);
            }
        }

        public void Read(ref PacketReader r)
        {
            Entries.Clear();
            Corpse = r.ReadGuid();
            Open = r.ReadBool();
            Money = r.ReadLong();
            int count = r.ReadByte();
            if (count > MaxEntries) throw new PacketException("Too many loot entries: " + count + ".");
            for (int i = 0; i < count; i++)
            {
                LootEntry e;
                e.Slot = r.ReadByte();
                e.ItemId = r.ReadUInt();
                e.Count = r.ReadUShort();
                Entries.Add(e);
            }
        }
    }

    /// <summary>A corpse the receiver may loot got (or ran out of) loot. Private: only those with loot rights get it.</summary>
    public sealed class LootableUpdate : IMessage
    {
        public EntityGuid Corpse;
        public bool Lootable;

        public Opcode Opcode => Opcode.LootableUpdate;

        public void Write(PacketWriter w)
        {
            w.WriteGuid(Corpse);
            w.WriteBool(Lootable);
        }

        public void Read(ref PacketReader r)
        {
            Corpse = r.ReadGuid();
            Lootable = r.ReadBool();
        }
    }

    /// <summary>What a vendor sells; prices are in the items' <see cref="ItemInfo"/>.</summary>
    public sealed class VendorInventory : IMessage
    {
        public const int MaxItems = 128;

        public EntityGuid Vendor;
        public readonly List<uint> ItemIds = new List<uint>();

        public Opcode Opcode => Opcode.VendorInventory;

        public void Write(PacketWriter w)
        {
            w.WriteGuid(Vendor);
            w.WriteUShort((ushort)ItemIds.Count);
            foreach (var id in ItemIds) w.WriteUInt(id);
        }

        public void Read(ref PacketReader r)
        {
            ItemIds.Clear();
            Vendor = r.ReadGuid();
            int count = r.ReadUShort();
            if (count > MaxItems) throw new PacketException("Too many vendor items: " + count + ".");
            for (int i = 0; i < count; i++) ItemIds.Add(r.ReadUInt());
        }
    }
}
