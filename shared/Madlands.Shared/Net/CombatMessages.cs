using System.Collections.Generic;
using Madlands.Shared.Game;

namespace Madlands.Shared.Net
{
    // Phase 3: targeting, spells, auras, combat, experience and chat.
    // Every message has a roundtrip test in Madlands.Server.Tests/ProtocolTests.cs.

    // ---------------------------------------------------------------- Client -> Server

    /// <summary>The player's current target (EntityGuid.Empty clears it). Visible to others as the unit's target.</summary>
    public sealed class SetTarget : IMessage
    {
        public EntityGuid Target;

        public Opcode Opcode => Opcode.SetTarget;
        public void Write(PacketWriter w) => w.WriteGuid(Target);
        public void Read(ref PacketReader r) => Target = r.ReadGuid();
    }

    /// <summary>
    /// Request to cast a spell. <see cref="CastId"/> is a client counter echoed in the replies, so
    /// the client can tell which of its requests failed.
    /// </summary>
    public sealed class CastSpell : IMessage
    {
        public uint SpellId;
        public EntityGuid Target;
        public byte CastId;

        public Opcode Opcode => Opcode.CastSpell;

        public void Write(PacketWriter w)
        {
            w.WriteUInt(SpellId);
            w.WriteGuid(Target);
            w.WriteByte(CastId);
        }

        public void Read(ref PacketReader r)
        {
            SpellId = r.ReadUInt();
            Target = r.ReadGuid();
            CastId = r.ReadByte();
        }
    }

    public sealed class CancelCast : IMessage
    {
        public Opcode Opcode => Opcode.CancelCast;
        public void Write(PacketWriter w) { }
        public void Read(ref PacketReader r) { }
    }

    /// <summary>Stops melee auto-attack (started by casting the Attack spell).</summary>
    public sealed class StopAttack : IMessage
    {
        public Opcode Opcode => Opcode.StopAttack;
        public void Write(PacketWriter w) { }
        public void Read(ref PacketReader r) { }
    }

    /// <summary>A line typed into chat. Lines starting with '.' are GM commands.</summary>
    public sealed class ChatInput : IMessage
    {
        public const int MaxTextBytes = 255;

        public string Text;

        public Opcode Opcode => Opcode.ChatInput;
        public void Write(PacketWriter w) => w.WriteString(Text);
        public void Read(ref PacketReader r) => Text = r.ReadString(MaxTextBytes);
    }

    /// <summary>A dead player gives up their body and returns to life at the map's graveyard.</summary>
    public sealed class ReleaseSpirit : IMessage
    {
        public Opcode Opcode => Opcode.ReleaseSpirit;
        public void Write(PacketWriter w) { }
        public void Read(ref PacketReader r) { }
    }

    /// <summary>Removes one of the player's own beneficial auras (right-click on a buff).</summary>
    public sealed class CancelAura : IMessage
    {
        public uint SpellId;

        public Opcode Opcode => Opcode.CancelAura;
        public void Write(PacketWriter w) => w.WriteUInt(SpellId);
        public void Read(ref PacketReader r) => SpellId = r.ReadUInt();
    }

    // ---------------------------------------------------------------- Server -> Client

    /// <summary>
    /// Display data for the client (spells, factions, classes, experience table, items, quests), sent once before
    /// <see cref="Welcome"/>. Keeping it on the server means content changes need no client rebuild;
    /// the client localizes names by id later (ADR 0008).
    /// </summary>
    public sealed class GameData : IMessage
    {
        public const int MaxEntries = 16384;

        public readonly List<SpellInfo> Spells = new List<SpellInfo>();
        public readonly List<FactionInfo> Factions = new List<FactionInfo>();
        public readonly List<ClassInfo> Classes = new List<ClassInfo>();
        /// <summary>Experience needed to go from level i+1 to i+2; its length + 1 is the level cap.</summary>
        public readonly List<int> XpToNextLevel = new List<int>();
        public readonly List<ItemInfo> Items = new List<ItemInfo>();
        public readonly List<QuestInfo> Quests = new List<QuestInfo>();

        public Opcode Opcode => Opcode.GameData;

        public void Write(PacketWriter w)
        {
            w.WriteUShort((ushort)Spells.Count);
            foreach (var s in Spells) s.Write(w);
            w.WriteUShort((ushort)Factions.Count);
            foreach (var f in Factions) f.Write(w);
            w.WriteUShort((ushort)Classes.Count);
            foreach (var c in Classes) c.Write(w);
            w.WriteUShort((ushort)XpToNextLevel.Count);
            foreach (var xp in XpToNextLevel) w.WriteInt(xp);
            w.WriteUShort((ushort)Items.Count);
            foreach (var i in Items) i.Write(w);
            w.WriteUShort((ushort)Quests.Count);
            foreach (var q in Quests) q.Write(w);
        }

        public void Read(ref PacketReader r)
        {
            Spells.Clear();
            Factions.Clear();
            Classes.Clear();
            XpToNextLevel.Clear();
            Items.Clear();
            Quests.Clear();
            int n = ReadCount(ref r);
            for (int i = 0; i < n; i++) Spells.Add(SpellInfo.Read(ref r));
            n = ReadCount(ref r);
            for (int i = 0; i < n; i++) Factions.Add(FactionInfo.Read(ref r));
            n = ReadCount(ref r);
            for (int i = 0; i < n; i++) Classes.Add(ClassInfo.Read(ref r));
            n = ReadCount(ref r);
            for (int i = 0; i < n; i++) XpToNextLevel.Add(r.ReadInt());
            n = ReadCount(ref r);
            for (int i = 0; i < n; i++) Items.Add(ItemInfo.Read(ref r));
            n = ReadCount(ref r);
            for (int i = 0; i < n; i++) Quests.Add(QuestInfo.Read(ref r));
        }

        private static int ReadCount(ref PacketReader r)
        {
            int n = r.ReadUShort();
            if (n > MaxEntries || n > r.Remaining) throw new PacketException("GameData list of " + n + " entries is too long.");
            return n;
        }
    }

    public struct UnitUpdateEntry
    {
        public EntityGuid Guid;
        public UnitFieldMask Mask;
        /// <summary>Source of the masked fields (writing) or the decoded fields (reading).</summary>
        public UnitValues Values;
    }

    /// <summary>Changed unit fields of several units, batched per broadcast.</summary>
    public sealed class UnitUpdates : IMessage
    {
        public readonly List<UnitUpdateEntry> Entries = new List<UnitUpdateEntry>();
        private readonly List<UnitValues> _pool = new List<UnitValues>();

        public Opcode Opcode => Opcode.UnitUpdates;

        public void Write(PacketWriter w)
        {
            w.WriteUShort((ushort)Entries.Count);
            foreach (var e in Entries)
            {
                w.WriteGuid(e.Guid);
                w.WriteUShort((ushort)e.Mask);
                e.Values.Write(w, e.Mask);
            }
        }

        public void Read(ref PacketReader r)
        {
            Entries.Clear();
            int count = r.ReadUShort();
            if (count * 10 > r.Remaining) throw new PacketException("UnitUpdates count " + count + " exceeds packet size.");
            for (int i = 0; i < count; i++)
            {
                if (_pool.Count <= i) _pool.Add(new UnitValues());
                UnitUpdateEntry e;
                e.Guid = r.ReadGuid();
                e.Mask = (UnitFieldMask)r.ReadUShort();
                e.Values = _pool[i];
                e.Values.Read(ref r, e.Mask);
                Entries.Add(e);
            }
        }
    }

    /// <summary>A unit began casting (cast bars, casting animations).</summary>
    public sealed class SpellStart : IMessage
    {
        public EntityGuid Caster;
        public EntityGuid Target;
        public uint SpellId;
        public byte CastId;
        public int CastTimeMs;

        public Opcode Opcode => Opcode.SpellStart;

        public void Write(PacketWriter w)
        {
            w.WriteGuid(Caster);
            w.WriteGuid(Target);
            w.WriteUInt(SpellId);
            w.WriteByte(CastId);
            w.WriteInt(CastTimeMs);
        }

        public void Read(ref PacketReader r)
        {
            Caster = r.ReadGuid();
            Target = r.ReadGuid();
            SpellId = r.ReadUInt();
            CastId = r.ReadByte();
            CastTimeMs = r.ReadInt();
        }
    }

    /// <summary>A cast completed and the spell was released (projectiles start flying now).</summary>
    public sealed class SpellGo : IMessage
    {
        public EntityGuid Caster;
        public EntityGuid Target;
        public uint SpellId;
        public byte CastId;

        public Opcode Opcode => Opcode.SpellGo;

        public void Write(PacketWriter w)
        {
            w.WriteGuid(Caster);
            w.WriteGuid(Target);
            w.WriteUInt(SpellId);
            w.WriteByte(CastId);
        }

        public void Read(ref PacketReader r)
        {
            Caster = r.ReadGuid();
            Target = r.ReadGuid();
            SpellId = r.ReadUInt();
            CastId = r.ReadByte();
        }
    }

    /// <summary>
    /// A cast request was refused, or a cast in progress ended without effect. The caster always gets
    /// it; observers only for casts they saw start (their cast bars stop).
    /// </summary>
    public sealed class SpellFailed : IMessage
    {
        public EntityGuid Caster;
        public uint SpellId;
        public byte CastId;
        public CastResult Result;

        public Opcode Opcode => Opcode.SpellFailed;

        public void Write(PacketWriter w)
        {
            w.WriteGuid(Caster);
            w.WriteUInt(SpellId);
            w.WriteByte(CastId);
            w.WriteByte((byte)Result);
        }

        public void Read(ref PacketReader r)
        {
            Caster = r.ReadGuid();
            SpellId = r.ReadUInt();
            CastId = r.ReadByte();
            Result = (CastResult)r.ReadByte();
        }
    }

    /// <summary>Damage, a heal or an avoided attack (floating combat text, combat log). SpellId 0 = melee swing.</summary>
    public sealed class CombatEvent : IMessage
    {
        public EntityGuid Source;
        public EntityGuid Target;
        public uint SpellId;
        public SpellSchool School;
        /// <summary>Health actually changed (damage taken or healing done), before overkill/overheal is removed.</summary>
        public int Amount;
        public int Absorbed;
        public CombatFlags Flags;

        public Opcode Opcode => Opcode.CombatEvent;

        public bool Has(CombatFlags flag) => (Flags & flag) != 0;

        public void Write(PacketWriter w)
        {
            w.WriteGuid(Source);
            w.WriteGuid(Target);
            w.WriteUInt(SpellId);
            w.WriteByte((byte)School);
            w.WriteInt(Amount);
            w.WriteInt(Absorbed);
            w.WriteUShort((ushort)Flags);
        }

        public void Read(ref PacketReader r)
        {
            Source = r.ReadGuid();
            Target = r.ReadGuid();
            SpellId = r.ReadUInt();
            School = (SpellSchool)r.ReadByte();
            Amount = r.ReadInt();
            Absorbed = r.ReadInt();
            Flags = (CombatFlags)r.ReadUShort();
        }
    }

    public struct AuraEntry
    {
        /// <summary>Slot on the unit; stable while the aura lasts.</summary>
        public byte Slot;
        /// <summary>0 = the slot was emptied.</summary>
        public uint SpellId;
        public EntityGuid Caster;
        public byte Stacks;
        /// <summary>0 = lasts until removed.</summary>
        public int DurationMs;
        public int RemainingMs;
    }

    /// <summary>Aura changes on one unit. With <see cref="Full"/> the list replaces everything the client had.</summary>
    public sealed class AuraUpdate : IMessage
    {
        public const int MaxAuras = 64;

        public EntityGuid Unit;
        public bool Full;
        public readonly List<AuraEntry> Entries = new List<AuraEntry>();

        public Opcode Opcode => Opcode.AuraUpdate;

        public void Write(PacketWriter w)
        {
            w.WriteGuid(Unit);
            w.WriteBool(Full);
            w.WriteByte((byte)Entries.Count);
            foreach (var e in Entries)
            {
                w.WriteByte(e.Slot);
                w.WriteUInt(e.SpellId);
                w.WriteGuid(e.Caster);
                w.WriteByte(e.Stacks);
                w.WriteInt(e.DurationMs);
                w.WriteInt(e.RemainingMs);
            }
        }

        public void Read(ref PacketReader r)
        {
            Entries.Clear();
            Unit = r.ReadGuid();
            Full = r.ReadBool();
            int count = r.ReadByte();
            if (count > MaxAuras) throw new PacketException("Too many auras: " + count + ".");
            for (int i = 0; i < count; i++)
            {
                AuraEntry e;
                e.Slot = r.ReadByte();
                e.SpellId = r.ReadUInt();
                e.Caster = r.ReadGuid();
                e.Stacks = r.ReadByte();
                e.DurationMs = r.ReadInt();
                e.RemainingMs = r.ReadInt();
                Entries.Add(e);
            }
        }
    }

    /// <summary>A spell's cooldown started (DurationMs &gt; 0) or was reset (0). SpellId 0 + 0 ms resets all.</summary>
    public sealed class Cooldown : IMessage
    {
        public uint SpellId;
        public int DurationMs;

        public Opcode Opcode => Opcode.Cooldown;

        public void Write(PacketWriter w)
        {
            w.WriteUInt(SpellId);
            w.WriteInt(DurationMs);
        }

        public void Read(ref PacketReader r)
        {
            SpellId = r.ReadUInt();
            DurationMs = r.ReadInt();
        }
    }

    /// <summary>The player's own detailed numbers (character sheet, experience bar). Only sent to the owner.</summary>
    public sealed class PlayerStats : IMessage
    {
        public const int StatCount = 8;

        public int Xp;
        public int XpToNextLevel;
        /// <summary>Indexed by <see cref="StatType"/>.</summary>
        public readonly int[] Stats = new int[StatCount];
        public int MeleeMinDamage;
        public int MeleeMaxDamage;
        public int MeleeSpeedMs;
        /// <summary>Percent, e.g. 5.5.</summary>
        public float CritChance;
        public float SpellCritChance;
        public float DodgeChance;

        public Opcode Opcode => Opcode.PlayerStats;

        public void Write(PacketWriter w)
        {
            w.WriteInt(Xp);
            w.WriteInt(XpToNextLevel);
            for (int i = 0; i < StatCount; i++) w.WriteInt(Stats[i]);
            w.WriteInt(MeleeMinDamage);
            w.WriteInt(MeleeMaxDamage);
            w.WriteInt(MeleeSpeedMs);
            w.WriteFloat(CritChance);
            w.WriteFloat(SpellCritChance);
            w.WriteFloat(DodgeChance);
        }

        public void Read(ref PacketReader r)
        {
            Xp = r.ReadInt();
            XpToNextLevel = r.ReadInt();
            for (int i = 0; i < StatCount; i++) Stats[i] = r.ReadInt();
            MeleeMinDamage = r.ReadInt();
            MeleeMaxDamage = r.ReadInt();
            MeleeSpeedMs = r.ReadInt();
            CritChance = r.ReadFloat();
            SpellCritChance = r.ReadFloat();
            DodgeChance = r.ReadFloat();
        }
    }

    /// <summary>All spells the player knows (sent at login and whenever it changes, e.g. on level up).</summary>
    public sealed class Spellbook : IMessage
    {
        public const int MaxSpells = 1024;

        public readonly List<uint> SpellIds = new List<uint>();

        public Opcode Opcode => Opcode.Spellbook;

        public void Write(PacketWriter w)
        {
            w.WriteUShort((ushort)SpellIds.Count);
            foreach (var id in SpellIds) w.WriteUInt(id);
        }

        public void Read(ref PacketReader r)
        {
            SpellIds.Clear();
            int count = r.ReadUShort();
            if (count > MaxSpells) throw new PacketException("Too many spells: " + count + ".");
            for (int i = 0; i < count; i++) SpellIds.Add(r.ReadUInt());
        }
    }

    public sealed class ChatMessage : IMessage
    {
        public const int MaxTextBytes = 2048;

        public ChatChannel Channel;
        public EntityGuid Sender;
        public string SenderName;
        public string Text;

        public Opcode Opcode => Opcode.ChatMessage;

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)Channel);
            w.WriteGuid(Sender);
            w.WriteString(SenderName);
            w.WriteString(Text);
        }

        public void Read(ref PacketReader r)
        {
            Channel = (ChatChannel)r.ReadByte();
            Sender = r.ReadGuid();
            SenderName = r.ReadString(Hello.MaxNameBytes);
            Text = r.ReadString(MaxTextBytes);
        }
    }

    public sealed class ExperienceGained : IMessage
    {
        public int Amount;
        /// <summary>The creature killed, or Empty for other sources (GM command, quests later).</summary>
        public EntityGuid Victim;

        public Opcode Opcode => Opcode.ExperienceGained;

        public void Write(PacketWriter w)
        {
            w.WriteInt(Amount);
            w.WriteGuid(Victim);
        }

        public void Read(ref PacketReader r)
        {
            Amount = r.ReadInt();
            Victim = r.ReadGuid();
        }
    }
}
