using Madlands.Content;
using Madlands.Server.World;
using Madlands.Shared;
using Madlands.Shared.Game;

namespace Madlands.Server.Combat;

/// <summary>An active aura on a unit. Its amounts are fixed when applied (from the caster's level then).</summary>
public sealed class Aura
{
    public Aura(SpellDefinition spell, EntityGuid caster, int casterLevel, byte slot)
    {
        Spell = spell;
        Caster = caster;
        CasterLevel = casterLevel;
        Slot = slot;
        Amounts = new float[spell.Aura!.Effects.Count];
    }

    public SpellDefinition Spell { get; }
    public AuraDefinition Definition => Spell.Aura!;
    public EntityGuid Caster { get; }
    public int CasterLevel { get; set; }
    public byte Slot { get; }
    public bool IsHarmful => Spell.Has(SpellFlags.HarmfulAura);

    public long AppliedMs;
    public long ExpiresMs;
    public long NextTickMs;
    public int Stacks = 1;
    /// <summary>Per aura effect: amount including the per-level part.</summary>
    public readonly float[] Amounts;
    public float AbsorbRemaining;

    public int DurationMs => Definition.DurationMs;
    public int RemainingMs(long now) => (int)Math.Max(0, ExpiresMs - now);

    public bool Has(AuraEffectType type)
    {
        foreach (var e in Definition.Effects) if (e.Type == type) return true;
        return false;
    }

    /// <summary>True if the aura changes stats, speed or control (needs a stat recalculation when added or removed).</summary>
    public bool AffectsStats
    {
        get
        {
            foreach (var e in Definition.Effects)
                if (e.Type is AuraEffectType.ModStat or AuraEffectType.ModSpeed or AuraEffectType.ModDamageDone or AuraEffectType.Stun or AuraEffectType.Root)
                    return true;
            return false;
        }
    }
}

/// <summary>A spell being cast (cast time &gt; 0).</summary>
public sealed class SpellCast
{
    public SpellCast(SpellDefinition spell, EntityGuid target, byte castId, long startMs, Vec3 startPosition)
    {
        Spell = spell;
        Target = target;
        CastId = castId;
        StartMs = startMs;
        EndMs = startMs + spell.CastTimeMs;
        StartPosition = startPosition;
    }

    public SpellDefinition Spell { get; }
    public EntityGuid Target { get; }
    public byte CastId { get; }
    public long StartMs { get; }
    public long EndMs { get; }
    public Vec3 StartPosition { get; }
}

/// <summary>A launched projectile that applies its spell's effects when it arrives.</summary>
public sealed class PendingHit
{
    public required SpellDefinition Spell { get; init; }
    public required EntityGuid Caster { get; init; }
    public required EntityGuid Target { get; init; }
    public required long HitAtMs { get; init; }
}

/// <summary>Who a creature is angry at, and how much. The top entry is its target.</summary>
public sealed class ThreatList
{
    /// <summary>A new target must exceed the current one's threat by this factor (stops target flip-flopping).</summary>
    public const float SwitchFactor = 1.1f;

    private readonly Dictionary<Unit, float> _threat = new();

    public int Count => _threat.Count;
    public IEnumerable<Unit> Units => _threat.Keys;
    public bool Contains(Unit unit) => _threat.ContainsKey(unit);
    public float Get(Unit unit) => _threat.GetValueOrDefault(unit);

    public void Add(Unit unit, float amount) => _threat[unit] = _threat.GetValueOrDefault(unit) + Math.Max(0f, amount);

    public bool Remove(Unit unit) => _threat.Remove(unit);

    public void Clear() => _threat.Clear();

    /// <summary>The unit to attack: the highest threat, sticking with <paramref name="current"/> unless clearly beaten.</summary>
    public Unit? SelectTarget(Unit? current)
    {
        Unit? best = null;
        float bestThreat = -1f;
        foreach (var (unit, threat) in _threat)
        {
            if (threat > bestThreat)
            {
                best = unit;
                bestThreat = threat;
            }
        }
        if (current is not null && best != current && _threat.TryGetValue(current, out float currentThreat) && bestThreat <= currentThreat * SwitchFactor)
            return current;
        return best;
    }
}

/// <summary>Experience rules (classic-style: same-level kills give 45 + 5 x level, fading to nothing for much weaker creatures).</summary>
public static class Experience
{
    public static int ForKill(int playerLevel, int creatureLevel, float multiplier = 1f)
    {
        if (multiplier <= 0f) return 0;
        float baseXp = playerLevel * 5f + 45f;
        float xp;
        if (creatureLevel >= playerLevel)
        {
            xp = baseXp * (1f + 0.05f * Math.Min(creatureLevel - playerLevel, 4));
        }
        else
        {
            if (creatureLevel <= GrayLevel(playerLevel)) return 0;
            xp = baseXp * (1f - (float)(playerLevel - creatureLevel) / ZeroDifference(playerLevel));
        }
        return Math.Max(0, (int)MathF.Round(xp * multiplier));
    }

    /// <summary>A quest's experience, falling off for players more than five levels above the quest.</summary>
    public static int ForQuest(int playerLevel, int questLevel, int xp)
    {
        int above = playerLevel - questLevel;
        float factor = above <= 5 ? 1f : above switch { 6 => 0.8f, 7 => 0.6f, 8 => 0.4f, 9 => 0.2f, _ => 0.1f };
        return Math.Max(0, (int)MathF.Round(xp * factor));
    }

    // Rested experience (as in WoW): a pool that grows while a character rests in a village (logged in or out) or,
    // slower, while logged out elsewhere. Kills give double experience until the bar reaches the rest mark: the pool
    // shrinks by all a kill gives, bonus included. Quest experience leaves it alone. Rules in levels.json ("rest").

    /// <summary>Rest built up over <paramref name="seconds"/> at a level whose bar is <paramref name="xpToNextLevel"/>.</summary>
    public static double RestGained(RestRules rules, int xpToNextLevel, double seconds, bool inRestArea) =>
        seconds <= 0 || xpToNextLevel <= 0 ? 0
            : rules.PercentPerHour / 100.0 * xpToNextLevel * (inRestArea ? 1.0 : rules.ElsewhereFactor) * seconds / 3600.0;

    public static double MaxRested(RestRules rules, int xpToNextLevel) => rules.MaxPercent / 100.0 * xpToNextLevel;

    /// <summary>The rested bonus on a kill worth <paramref name="xp"/>; takes the kill and the bonus from <paramref name="rested"/>.</summary>
    public static int RestedBonus(int xp, ref double rested)
    {
        if (xp <= 0 || rested <= 0) return 0;
        int bonus = (int)Math.Min(xp, Math.Floor(rested / 2));
        rested = Math.Max(0, rested - xp - bonus);
        return bonus;
    }

    /// <summary>Creatures at or below this level give no experience.</summary>
    public static int GrayLevel(int playerLevel) => playerLevel <= 5 ? 0 : playerLevel - 5 - playerLevel / 10;

    private static int ZeroDifference(int playerLevel) => playerLevel switch
    {
        < 8 => 5,
        < 10 => 6,
        < 12 => 7,
        < 16 => 8,
        < 20 => 9,
        _ => 10,
    };
}
