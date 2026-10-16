using System;

namespace Madlands.Shared.Game
{
    // Enums that travel on the wire or appear in content. Values are stable: append, never renumber.

    public enum PowerType : byte
    {
        None = 0,
        Mana = 1,
        Rage = 2,
        Energy = 3,
    }

    public enum SpellSchool : byte
    {
        Physical = 0,
        Arcane = 1,
        Fire = 2,
        Frost = 3,
        Nature = 4,
        Holy = 5,
        Shadow = 6,
    }

    /// <summary>What a spell must be cast on.</summary>
    public enum SpellTarget : byte
    {
        /// <summary>Always the caster; no target needed.</summary>
        Self = 0,
        /// <summary>A hostile or neutral unit.</summary>
        Enemy = 1,
        /// <summary>A friendly unit; falls back to the caster when the target is not friendly.</summary>
        Friend = 2,
        /// <summary>No target: the effects pick their own (e.g. enemies around the caster).</summary>
        None = 3,
    }

    [Flags]
    public enum SpellFlags : uint
    {
        None = 0,
        /// <summary>The target must be in front of the caster.</summary>
        RequiresFacing = 1 << 0,
        /// <summary>Skips the line-of-sight check.</summary>
        IgnoreLineOfSight = 1 << 1,
        /// <summary>Starts melee auto-attack instead of doing anything itself.</summary>
        AutoAttack = 1 << 2,
        /// <summary>The spell's aura is harmful (shown as a debuff, cannot be cancelled).</summary>
        HarmfulAura = 1 << 3,
        /// <summary>Not shown in the spellbook (creature abilities, internal spells).</summary>
        Hidden = 1 << 4,
    }

    /// <summary>Why a cast did not happen (or ended early). Shown to the player as an error text.</summary>
    public enum CastResult : byte
    {
        Success = 0,
        Interrupted = 1,
        NotKnown = 2,
        OnCooldown = 3,
        NotReady = 4,
        NoPower = 5,
        NoTarget = 6,
        InvalidTarget = 7,
        OutOfRange = 8,
        TooClose = 9,
        NotInLineOfSight = 10,
        NotFacing = 11,
        Moving = 12,
        CasterDead = 13,
        Stunned = 14,
        AlreadyCasting = 15,
        TargetDead = 16,
        Cancelled = 17,
    }

    public enum Reaction : byte
    {
        Hostile = 0,
        Neutral = 1,
        Friendly = 2,
    }

    [Flags]
    public enum UnitFlags : uint
    {
        None = 0,
        Dead = 1 << 0,
        InCombat = 1 << 1,
        /// <summary>A creature that gave up the fight and is running home; immune until it gets there.</summary>
        Evading = 1 << 2,
        Stunned = 1 << 3,
        Rooted = 1 << 4,
        AutoAttacking = 1 << 5,
        /// <summary>Takes no damage (evading creature, GM god mode).</summary>
        Immune = 1 << 6,
    }

    /// <summary>How a combat event turned out. Damage and heals share one message.</summary>
    [Flags]
    public enum CombatFlags : ushort
    {
        None = 0,
        Heal = 1 << 0,
        Crit = 1 << 1,
        Miss = 1 << 2,
        Dodge = 1 << 3,
        Parry = 1 << 4,
        Immune = 1 << 5,
        Evade = 1 << 6,
        /// <summary>A tick of a damage- or heal-over-time aura.</summary>
        Periodic = 1 << 7,
        /// <summary>Part or all of the damage was absorbed (see Absorbed).</summary>
        Absorb = 1 << 8,
        KillingBlow = 1 << 9,
    }

    public enum ChatChannel : byte
    {
        /// <summary>Server messages: GM command output, notices.</summary>
        System = 0,
        /// <summary>Heard by everyone within <see cref="GameConstants.SayRange"/>.</summary>
        Say = 1,
        /// <summary>An error shown in the middle of the screen (red text), e.g. "Out of range".</summary>
        Error = 2,
    }

    public enum StatType : byte
    {
        Strength = 0,
        Agility = 1,
        Stamina = 2,
        Intellect = 3,
        Spirit = 4,
        Armor = 5,
        AttackPower = 6,
        SpellPower = 7,
    }
}
