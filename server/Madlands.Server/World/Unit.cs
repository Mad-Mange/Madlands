using Madlands.Server.Combat;
using Madlands.Shared;
using Madlands.Shared.Game;

namespace Madlands.Server.World;

/// <summary>
/// An entity that fights: players and creatures. Holds the public <see cref="UnitValues"/> (with
/// a dirty mask that the map broadcasts), computed stats, auras, casting and auto-attack state.
/// The rules that change this state live in the map's systems (CombatSystem, SpellSystem, AuraSystem).
/// </summary>
public abstract class Unit : Entity
{
    protected Unit(EntityGuid guid, string name, uint displayId)
        : base(guid, name, displayId)
    {
    }

    public abstract bool IsPlayer { get; }

    /// <summary>Public state. Change it through the properties below so the dirty mask stays right.</summary>
    public UnitValues Values { get; } = new();
    /// <summary>Fields changed since the last broadcast.</summary>
    public UnitFieldMask Dirty;

    public UnitStats Stats { get; } = new();
    public List<Aura> Auras { get; } = new();

    /// <summary>The spell being cast, if any.</summary>
    public SpellCast? Casting;
    public long GcdEndsMs;
    /// <summary>spellId -> server time when it is ready again.</summary>
    public Dictionary<uint, long> Cooldowns { get; } = new();

    /// <summary>Melee auto-attack target (null = not attacking).</summary>
    public Unit? AttackTarget;
    public long NextSwingMs;
    /// <summary>Stays in combat at least until this time (refreshed by any hostile action).</summary>
    public long CombatUntilMs;
    public long NextRegenMs;
    public long LastManaSpendMs = long.MinValue / 2;

    // ------------------------------------------------------------------ values

    public byte Level
    {
        get => Values.Level;
        set { if (Values.Level != value) { Values.Level = value; Dirty |= UnitFieldMask.Level; } }
    }

    public int Health
    {
        get => Values.Health;
        set
        {
            int v = Math.Clamp(value, 0, Values.MaxHealth);
            if (Values.Health != v) { Values.Health = v; Dirty |= UnitFieldMask.Health; }
        }
    }

    public int MaxHealth
    {
        get => Values.MaxHealth;
        set
        {
            if (Values.MaxHealth != value) { Values.MaxHealth = value; Dirty |= UnitFieldMask.MaxHealth; }
            if (Values.Health > value) Health = value;
        }
    }

    public PowerType PowerType
    {
        get => Values.PowerType;
        set { if (Values.PowerType != value) { Values.PowerType = value; Dirty |= UnitFieldMask.PowerType; } }
    }

    public int Power
    {
        get => Values.Power;
        set
        {
            int v = Math.Clamp(value, 0, Values.MaxPower);
            if (Values.Power != v) { Values.Power = v; Dirty |= UnitFieldMask.Power; }
        }
    }

    public int MaxPower
    {
        get => Values.MaxPower;
        set
        {
            if (Values.MaxPower != value) { Values.MaxPower = value; Dirty |= UnitFieldMask.MaxPower; }
            if (Values.Power > value) Power = value;
        }
    }

    public ushort Faction
    {
        get => Values.Faction;
        set { if (Values.Faction != value) { Values.Faction = value; Dirty |= UnitFieldMask.Faction; } }
    }

    public EntityGuid TargetGuid
    {
        get => Values.Target;
        set { if (Values.Target != value) { Values.Target = value; Dirty |= UnitFieldMask.Target; } }
    }

    public ushort SpeedPercent
    {
        get => Values.SpeedPercent;
        set { if (Values.SpeedPercent != value) { Values.SpeedPercent = value; Dirty |= UnitFieldMask.SpeedPercent; } }
    }

    public byte ClassId
    {
        get => Values.ClassId;
        set { if (Values.ClassId != value) { Values.ClassId = value; Dirty |= UnitFieldMask.ClassId; } }
    }

    public UnitFlags Flags => Values.Flags;
    public bool HasFlag(UnitFlags flag) => (Values.Flags & flag) != 0;

    public void SetFlag(UnitFlags flag, bool on)
    {
        var flags = on ? Values.Flags | flag : Values.Flags & ~flag;
        if (flags != Values.Flags) { Values.Flags = flags; Dirty |= UnitFieldMask.Flags; }
    }

    public bool IsAlive => !HasFlag(UnitFlags.Dead);
    public bool IsDead => HasFlag(UnitFlags.Dead);
    public bool IsInCombat => HasFlag(UnitFlags.InCombat);
    /// <summary>Cannot act at all.</summary>
    public bool IsStunned => HasFlag(UnitFlags.Stunned);
    /// <summary>Cannot move (stunned, rooted or dead).</summary>
    public bool IsImmobile => HasFlag(UnitFlags.Stunned | UnitFlags.Rooted | UnitFlags.Dead);
    public float SpeedMultiplier => SpeedPercent / 100f;

    // ------------------------------------------------------------------ geometry

    public float Yaw => Movement.Yaw;
    public Vec3 EyePosition => Position + new Vec3(0, GameConstants.EyeHeight, 0);

    /// <summary>True if <paramref name="other"/> is within the front cone of this unit's facing.</summary>
    public bool IsInFront(Unit other) => IsInFront(other.Position);

    public bool IsInFront(Vec3 point)
    {
        var to = (point - Position).Horizontal;
        if (to.HorizontalLengthSquared < 0.01f) return true;
        float angle = MathF.Abs(MathUtil.WrapAngle(MathUtil.DirectionToYaw(to.X, to.Z) - Yaw));
        return angle <= GameConstants.FacingHalfAngle;
    }

    public float DistanceTo(Unit other) => Vec3.Distance(Position, other.Position);

    /// <summary>Recomputes stats from level, class/template and auras; updates max health/power, speed and control flags.</summary>
    public abstract void RecalculateStats();

    /// <summary>Applies the aura-derived parts shared by players and creatures.</summary>
    protected void ApplyAuraModifiers(AuraModifiers mods)
    {
        Stats.DamageDoneMultiplier = Math.Max(0f, 1f + mods.DamageDonePercent / 100f);
        int speed = 100 + (int)MathF.Round(mods.SpeedBoostPercent + mods.SpeedSlowPercent);
        SpeedPercent = (ushort)Math.Clamp(speed, 10, 300);
        SetFlag(UnitFlags.Stunned, mods.Stunned);
        SetFlag(UnitFlags.Rooted, mods.Rooted);
    }
}
