using System;
using Madlands.Shared.Net;

namespace Madlands.Shared.Game
{
    /// <summary>Bit per <see cref="UnitValues"/> field; fields are written in bit order.</summary>
    [Flags]
    public enum UnitFieldMask : ushort
    {
        None = 0,
        Level = 1 << 0,
        Health = 1 << 1,
        MaxHealth = 1 << 2,
        PowerType = 1 << 3,
        Power = 1 << 4,
        MaxPower = 1 << 5,
        Faction = 1 << 6,
        Flags = 1 << 7,
        Target = 1 << 8,
        SpeedPercent = 1 << 9,
        ClassId = 1 << 10,
        /// <summary><see cref="UnitValues.NpcFlags"/> and <see cref="UnitValues.NpcRole"/>.</summary>
        NpcFlags = 1 << 11,
        MainHandDisplay = 1 << 12,
        OffHandDisplay = 1 << 13,
        /// <summary>All of <see cref="UnitValues.Gear"/> (it changes rarely, so it travels as one field).</summary>
        Gear = 1 << 14,

        All = Level | Health | MaxHealth | PowerType | Power | MaxPower | Faction | Flags | Target | SpeedPercent | ClassId |
              NpcFlags | MainHandDisplay | OffHandDisplay | Gear,
    }

    /// <summary>The armor slots other players can see, in the order of <see cref="UnitValues.Gear"/>.</summary>
    public static class VisibleGear
    {
        public const int Count = 8;

        public static readonly EquipSlot[] Slots =
        {
            EquipSlot.Head, EquipSlot.Shoulders, EquipSlot.Back, EquipSlot.Chest,
            EquipSlot.Hands, EquipSlot.Waist, EquipSlot.Legs, EquipSlot.Feet,
        };
    }

    /// <summary>
    /// The publicly visible state of a unit (player or creature): what unit frames, nameplates and
    /// other clients need. Sent in full with a spawn, then as masked deltas (<see cref="UnitUpdates"/>).
    /// Private details (stats, experience) go only to the owning player.
    /// </summary>
    public sealed class UnitValues
    {
        public byte Level = 1;
        public int Health;
        public int MaxHealth;
        public PowerType PowerType;
        public int Power;
        public int MaxPower;
        public ushort Faction;
        public UnitFlags Flags;
        public EntityGuid Target;
        /// <summary>Movement speed in percent of normal (slows and sprints change it).</summary>
        public ushort SpeedPercent = 100;
        /// <summary>Player class id (content/classes.json), 0 for creatures.</summary>
        public byte ClassId;
        /// <summary>What talking to this NPC offers (quests, a vendor); None for players and most creatures.</summary>
        public NpcFlags NpcFlags;
        /// <summary>What the NPC is (its symbol and title); None for players and most creatures.</summary>
        public NpcRole NpcRole;
        /// <summary>Display id of the weapon in the main hand (0 = empty hands).</summary>
        public uint MainHandDisplay;
        /// <summary>Display id of the shield or held item in the off hand.</summary>
        public uint OffHandDisplay;
        /// <summary>Display ids of the worn armor others can see (<see cref="VisibleGear.Slots"/>; 0 = nothing).</summary>
        public readonly uint[] Gear = new uint[VisibleGear.Count];

        public bool IsDead => (Flags & UnitFlags.Dead) != 0;
        public bool Has(UnitFlags flag) => (Flags & flag) != 0;
        public float HealthFraction => MaxHealth > 0 ? (float)Health / MaxHealth : 0f;
        public float PowerFraction => MaxPower > 0 ? (float)Power / MaxPower : 0f;

        public void CopyFrom(UnitValues other)
        {
            Level = other.Level;
            Health = other.Health;
            MaxHealth = other.MaxHealth;
            PowerType = other.PowerType;
            Power = other.Power;
            MaxPower = other.MaxPower;
            Faction = other.Faction;
            Flags = other.Flags;
            Target = other.Target;
            SpeedPercent = other.SpeedPercent;
            ClassId = other.ClassId;
            NpcFlags = other.NpcFlags;
            NpcRole = other.NpcRole;
            MainHandDisplay = other.MainHandDisplay;
            OffHandDisplay = other.OffHandDisplay;
            Array.Copy(other.Gear, Gear, VisibleGear.Count);
        }

        /// <summary>Copies only the fields in <paramref name="mask"/> (applying a delta).</summary>
        public void CopyFrom(UnitValues other, UnitFieldMask mask)
        {
            if ((mask & UnitFieldMask.Level) != 0) Level = other.Level;
            if ((mask & UnitFieldMask.Health) != 0) Health = other.Health;
            if ((mask & UnitFieldMask.MaxHealth) != 0) MaxHealth = other.MaxHealth;
            if ((mask & UnitFieldMask.PowerType) != 0) PowerType = other.PowerType;
            if ((mask & UnitFieldMask.Power) != 0) Power = other.Power;
            if ((mask & UnitFieldMask.MaxPower) != 0) MaxPower = other.MaxPower;
            if ((mask & UnitFieldMask.Faction) != 0) Faction = other.Faction;
            if ((mask & UnitFieldMask.Flags) != 0) Flags = other.Flags;
            if ((mask & UnitFieldMask.Target) != 0) Target = other.Target;
            if ((mask & UnitFieldMask.SpeedPercent) != 0) SpeedPercent = other.SpeedPercent;
            if ((mask & UnitFieldMask.ClassId) != 0) ClassId = other.ClassId;
            if ((mask & UnitFieldMask.NpcFlags) != 0)
            {
                NpcFlags = other.NpcFlags;
                NpcRole = other.NpcRole;
            }
            if ((mask & UnitFieldMask.MainHandDisplay) != 0) MainHandDisplay = other.MainHandDisplay;
            if ((mask & UnitFieldMask.OffHandDisplay) != 0) OffHandDisplay = other.OffHandDisplay;
            if ((mask & UnitFieldMask.Gear) != 0) Array.Copy(other.Gear, Gear, VisibleGear.Count);
        }

        public void Write(PacketWriter w, UnitFieldMask mask)
        {
            if ((mask & UnitFieldMask.Level) != 0) w.WriteByte(Level);
            if ((mask & UnitFieldMask.Health) != 0) w.WriteInt(Health);
            if ((mask & UnitFieldMask.MaxHealth) != 0) w.WriteInt(MaxHealth);
            if ((mask & UnitFieldMask.PowerType) != 0) w.WriteByte((byte)PowerType);
            if ((mask & UnitFieldMask.Power) != 0) w.WriteInt(Power);
            if ((mask & UnitFieldMask.MaxPower) != 0) w.WriteInt(MaxPower);
            if ((mask & UnitFieldMask.Faction) != 0) w.WriteUShort(Faction);
            if ((mask & UnitFieldMask.Flags) != 0) w.WriteUInt((uint)Flags);
            if ((mask & UnitFieldMask.Target) != 0) w.WriteGuid(Target);
            if ((mask & UnitFieldMask.SpeedPercent) != 0) w.WriteUShort(SpeedPercent);
            if ((mask & UnitFieldMask.ClassId) != 0) w.WriteByte(ClassId);
            if ((mask & UnitFieldMask.NpcFlags) != 0)
            {
                w.WriteByte((byte)NpcFlags);
                w.WriteByte((byte)NpcRole);
            }
            if ((mask & UnitFieldMask.MainHandDisplay) != 0) w.WriteUInt(MainHandDisplay);
            if ((mask & UnitFieldMask.OffHandDisplay) != 0) w.WriteUInt(OffHandDisplay);
            if ((mask & UnitFieldMask.Gear) != 0)
                for (int i = 0; i < VisibleGear.Count; i++) w.WriteVarULong(Gear[i]);
        }

        public void Read(ref PacketReader r, UnitFieldMask mask)
        {
            if ((mask & ~UnitFieldMask.All) != 0) throw new PacketException("Unknown unit fields in mask " + (ushort)mask + ".");
            if ((mask & UnitFieldMask.Level) != 0) Level = r.ReadByte();
            if ((mask & UnitFieldMask.Health) != 0) Health = r.ReadInt();
            if ((mask & UnitFieldMask.MaxHealth) != 0) MaxHealth = r.ReadInt();
            if ((mask & UnitFieldMask.PowerType) != 0) PowerType = (PowerType)r.ReadByte();
            if ((mask & UnitFieldMask.Power) != 0) Power = r.ReadInt();
            if ((mask & UnitFieldMask.MaxPower) != 0) MaxPower = r.ReadInt();
            if ((mask & UnitFieldMask.Faction) != 0) Faction = r.ReadUShort();
            if ((mask & UnitFieldMask.Flags) != 0) Flags = (UnitFlags)r.ReadUInt();
            if ((mask & UnitFieldMask.Target) != 0) Target = r.ReadGuid();
            if ((mask & UnitFieldMask.SpeedPercent) != 0) SpeedPercent = r.ReadUShort();
            if ((mask & UnitFieldMask.ClassId) != 0) ClassId = r.ReadByte();
            if ((mask & UnitFieldMask.NpcFlags) != 0)
            {
                NpcFlags = (NpcFlags)r.ReadByte();
                NpcRole = (NpcRole)r.ReadByte();
            }
            if ((mask & UnitFieldMask.MainHandDisplay) != 0) MainHandDisplay = r.ReadUInt();
            if ((mask & UnitFieldMask.OffHandDisplay) != 0) OffHandDisplay = r.ReadUInt();
            if ((mask & UnitFieldMask.Gear) != 0)
            {
                for (int i = 0; i < VisibleGear.Count; i++)
                {
                    ulong display = r.ReadVarULong();
                    if (display > uint.MaxValue) throw new PacketException("Gear display id out of range.");
                    Gear[i] = (uint)display;
                }
            }
        }

        public bool ValueEquals(UnitValues o) =>
            Level == o.Level && Health == o.Health && MaxHealth == o.MaxHealth && PowerType == o.PowerType &&
            Power == o.Power && MaxPower == o.MaxPower && Faction == o.Faction && Flags == o.Flags &&
            Target == o.Target && SpeedPercent == o.SpeedPercent && ClassId == o.ClassId && NpcFlags == o.NpcFlags && NpcRole == o.NpcRole &&
            MainHandDisplay == o.MainHandDisplay && OffHandDisplay == o.OffHandDisplay && GearEquals(o);

        private bool GearEquals(UnitValues o)
        {
            for (int i = 0; i < VisibleGear.Count; i++)
                if (Gear[i] != o.Gear[i]) return false;
            return true;
        }
    }
}
