using System.Collections.Generic;
using Madlands.Shared.Net;

namespace Madlands.Shared.Game
{
    /// <summary>
    /// What the client needs to know about a spell: names and numbers for the action bar, tooltips,
    /// cast bars, range checks and aura icons. The effects themselves stay on the server.
    /// </summary>
    public sealed class SpellInfo
    {
        public const int MaxNameBytes = 64;
        public const int MaxDescriptionBytes = 512;

        public uint Id;
        public string Name;
        public string Description;
        /// <summary>Client-side icon lookup id (like displayIds: the server never names assets).</summary>
        public uint Icon;
        public SpellSchool School;
        public SpellTarget Target;
        public SpellFlags Flags;
        public float MinRange;
        public float MaxRange;
        public int CastTimeMs;
        public int CooldownMs;
        public int GcdMs;
        public PowerType PowerType;
        public int Cost;
        /// <summary>Duration of the spell's aura, 0 if it has none.</summary>
        public int AuraDurationMs;
        /// <summary>Meters per second; 0 = hits instantly.</summary>
        public float ProjectileSpeed;
        /// <summary>Level at which the class learns it (0 for creature spells).</summary>
        public byte LearnLevel;

        public bool Has(SpellFlags flag) => (Flags & flag) != 0;
        public bool IsAutoAttack => (Flags & SpellFlags.AutoAttack) != 0;
        public bool HasAura => AuraDurationMs > 0;

        public void Write(PacketWriter w)
        {
            w.WriteUInt(Id);
            w.WriteString(Name);
            w.WriteString(Description);
            w.WriteUInt(Icon);
            w.WriteByte((byte)School);
            w.WriteByte((byte)Target);
            w.WriteUInt((uint)Flags);
            w.WriteFloat(MinRange);
            w.WriteFloat(MaxRange);
            w.WriteInt(CastTimeMs);
            w.WriteInt(CooldownMs);
            w.WriteInt(GcdMs);
            w.WriteByte((byte)PowerType);
            w.WriteInt(Cost);
            w.WriteInt(AuraDurationMs);
            w.WriteFloat(ProjectileSpeed);
            w.WriteByte(LearnLevel);
        }

        public static SpellInfo Read(ref PacketReader r)
        {
            var s = new SpellInfo();
            s.Id = r.ReadUInt();
            s.Name = r.ReadString(MaxNameBytes);
            s.Description = r.ReadString(MaxDescriptionBytes);
            s.Icon = r.ReadUInt();
            s.School = (SpellSchool)r.ReadByte();
            s.Target = (SpellTarget)r.ReadByte();
            s.Flags = (SpellFlags)r.ReadUInt();
            s.MinRange = r.ReadFloat();
            s.MaxRange = r.ReadFloat();
            s.CastTimeMs = r.ReadInt();
            s.CooldownMs = r.ReadInt();
            s.GcdMs = r.ReadInt();
            s.PowerType = (PowerType)r.ReadByte();
            s.Cost = r.ReadInt();
            s.AuraDurationMs = r.ReadInt();
            s.ProjectileSpeed = r.ReadFloat();
            s.LearnLevel = r.ReadByte();
            return s;
        }
    }

    public sealed class FactionInfo
    {
        public ushort Id;
        public string Name;
        public ushort[] Friendly = new ushort[0];
        public ushort[] Hostile = new ushort[0];

        public void Write(PacketWriter w)
        {
            w.WriteUShort(Id);
            w.WriteString(Name);
            WriteIds(w, Friendly);
            WriteIds(w, Hostile);
        }

        public static FactionInfo Read(ref PacketReader r)
        {
            var f = new FactionInfo();
            f.Id = r.ReadUShort();
            f.Name = r.ReadString(SpellInfo.MaxNameBytes);
            f.Friendly = ReadIds(ref r);
            f.Hostile = ReadIds(ref r);
            return f;
        }

        private static void WriteIds(PacketWriter w, ushort[] ids)
        {
            w.WriteUShort((ushort)ids.Length);
            foreach (var id in ids) w.WriteUShort(id);
        }

        private static ushort[] ReadIds(ref PacketReader r)
        {
            int count = r.ReadUShort();
            if (count * 2 > r.Remaining) throw new PacketException("Faction list longer than packet.");
            var ids = new ushort[count];
            for (int i = 0; i < count; i++) ids[i] = r.ReadUShort();
            return ids;
        }
    }

    /// <summary>
    /// Who likes whom. Shared so server (attack rules, aggro) and client (nameplate colors, which
    /// spells are usable) compute reactions with the same code.
    /// A unit is always friendly to its own faction; otherwise hostile if either side lists the
    /// other as hostile, friendly if either lists it as friendly, else neutral.
    /// </summary>
    public sealed class FactionTable
    {
        private readonly Dictionary<ushort, FactionInfo> _factions = new Dictionary<ushort, FactionInfo>();

        public FactionTable() { }

        public FactionTable(IEnumerable<FactionInfo> factions)
        {
            foreach (var f in factions) Add(f);
        }

        public IEnumerable<FactionInfo> All => _factions.Values;
        public bool Contains(ushort id) => _factions.ContainsKey(id);
        public void Add(FactionInfo faction) => _factions[faction.Id] = faction;

        public Reaction GetReaction(ushort a, ushort b)
        {
            if (a == b) return Reaction.Friendly;
            _factions.TryGetValue(a, out var fa);
            _factions.TryGetValue(b, out var fb);
            if (Lists(fa?.Hostile, b) || Lists(fb?.Hostile, a)) return Reaction.Hostile;
            if (Lists(fa?.Friendly, b) || Lists(fb?.Friendly, a)) return Reaction.Friendly;
            return Reaction.Neutral;
        }

        private static bool Lists(ushort[] ids, ushort id)
        {
            if (ids == null) return false;
            foreach (var x in ids) if (x == id) return true;
            return false;
        }
    }

    public struct ClassInfo
    {
        public byte Id;
        public string Name;
        public PowerType PowerType;

        public void Write(PacketWriter w)
        {
            w.WriteByte(Id);
            w.WriteString(Name);
            w.WriteByte((byte)PowerType);
        }

        public static ClassInfo Read(ref PacketReader r)
        {
            ClassInfo c;
            c.Id = r.ReadByte();
            c.Name = r.ReadString(SpellInfo.MaxNameBytes);
            c.PowerType = (PowerType)r.ReadByte();
            return c;
        }
    }
}
