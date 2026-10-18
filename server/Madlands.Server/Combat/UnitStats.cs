using Madlands.Content;
using Madlands.Server.Items;
using Madlands.Server.World;
using Madlands.Shared.Game;

namespace Madlands.Server.Combat;

/// <summary>A unit's effective numbers after level, class/template and auras (ADR 0005: base + modifiers, flat + percent).</summary>
public sealed class UnitStats
{
    public const int Count = 8;

    /// <summary>Indexed by <see cref="StatType"/>.</summary>
    public readonly float[] Values = new float[Count];

    public float this[StatType stat] => Values[(int)stat];
    public float Strength => Values[(int)StatType.Strength];
    public float Agility => Values[(int)StatType.Agility];
    public float Stamina => Values[(int)StatType.Stamina];
    public float Intellect => Values[(int)StatType.Intellect];
    public float Spirit => Values[(int)StatType.Spirit];
    public int Armor => (int)Values[(int)StatType.Armor];
    public int AttackPower => (int)Values[(int)StatType.AttackPower];
    public int SpellPower => (int)Values[(int)StatType.SpellPower];

    /// <summary>Percent chances.</summary>
    public float CritChance;
    public float SpellCritChance;
    public float DodgeChance;
    public float ParryChance;

    /// <summary>Damage of one normal melee swing before attack power.</summary>
    public float WeaponMin;
    public float WeaponMax;
    public int AttackSpeedMs = 2000;
    public float DamageDoneMultiplier = 1f;

    /// <summary>Attack power adds AP/14 damage per second of weapon speed (the classic rule).</summary>
    public float AttackPowerBonus => AttackPower / 14f * (AttackSpeedMs / 1000f);
}

/// <summary>What a unit's auras add up to; collected before each stat recalculation.</summary>
public sealed class AuraModifiers
{
    public readonly float[] Flat = new float[UnitStats.Count];
    public readonly float[] Percent = new float[UnitStats.Count];
    /// <summary>The strongest slow (negative) and the strongest boost (positive); slows do not stack.</summary>
    public float SpeedSlowPercent;
    public float SpeedBoostPercent;
    public float DamageDonePercent;
    public bool Stunned;
    public bool Rooted;

    public void Clear()
    {
        Array.Clear(Flat);
        Array.Clear(Percent);
        SpeedSlowPercent = 0;
        SpeedBoostPercent = 0;
        DamageDonePercent = 0;
        Stunned = false;
        Rooted = false;
    }

    public void Collect(List<Aura> auras)
    {
        Clear();
        foreach (var aura in auras)
        {
            var effects = aura.Definition.Effects;
            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                float amount = aura.Amounts[i] * aura.Stacks;
                switch (e.Type)
                {
                    case AuraEffectType.ModStat when e.Stat is { } stat:
                        if (e.Percent) Percent[(int)stat] += amount;
                        else Flat[(int)stat] += amount;
                        break;
                    case AuraEffectType.ModSpeed:
                        if (amount < 0) SpeedSlowPercent = Math.Min(SpeedSlowPercent, amount);
                        else SpeedBoostPercent = Math.Max(SpeedBoostPercent, amount);
                        break;
                    case AuraEffectType.ModDamageDone:
                        DamageDonePercent += amount;
                        break;
                    case AuraEffectType.Stun:
                        Stunned = true;
                        break;
                    case AuraEffectType.Root:
                        Rooted = true;
                        break;
                }
            }
        }
    }

    public float Apply(StatType stat, float baseValue) => (baseValue + Flat[(int)stat]) * (1f + Percent[(int)stat] / 100f);
}

/// <summary>The formulas. Kept in one place so balancing means editing one file (and content).</summary>
public static class StatFormulas
{
    public const int MaxRage = 100;
    public const int MaxEnergy = 100;

    /// <summary>
    /// Base (class, race, level) plus the flat stats of worn items, then aura modifiers (flat, then percent).
    /// The main-hand weapon sets the swing damage and speed; without one the class fights unarmed.
    /// </summary>
    public static void ComputePlayer(UnitStats s, ClassDefinition cls, RaceDefinition race, int level, AuraModifiers mods, Inventory? gear = null)
    {
        int levelsAbove1 = Math.Max(0, level - 1);
        Span<float> items = stackalloc float[UnitStats.Count];
        ItemWeaponDefinition? weapon = null;
        if (gear is not null)
        {
            for (var slot = EquipSlot.Head; slot <= EquipSlot.OffHand; slot++)
            {
                if (gear.Equipped(slot) is not { } worn) continue;
                for (int i = 0; i < UnitStats.Count; i++) items[i] += worn.Item.Stats[(StatType)i];
                if (slot == EquipSlot.MainHand) weapon = worn.Item.Weapon;
            }
        }

        for (int i = 0; i <= (int)StatType.Spirit; i++)
        {
            var stat = (StatType)i;
            float baseValue = cls.BaseStats[stat] + cls.StatsPerLevel[stat] * levelsAbove1 + race.Stats[stat] + items[i];
            s.Values[i] = MathF.Max(0f, mods.Apply(stat, baseValue));
        }
        s.Values[(int)StatType.Armor] = MathF.Max(0f, mods.Apply(StatType.Armor, s.Agility * 2f + items[(int)StatType.Armor]));
        s.Values[(int)StatType.AttackPower] = MathF.Max(0f, mods.Apply(StatType.AttackPower,
            MathF.Max(0f, s.Strength * 2f - 20f) + items[(int)StatType.AttackPower]));
        s.Values[(int)StatType.SpellPower] = MathF.Max(0f, mods.Apply(StatType.SpellPower, items[(int)StatType.SpellPower]));
        s.CritChance = 5f + s.Agility / 20f;
        s.SpellCritChance = 5f + s.Intellect / 30f;
        s.DodgeChance = 5f + s.Agility / 20f;
        s.ParryChance = 5f;
        if (weapon is not null)
        {
            s.WeaponMin = weapon.MinDamage;
            s.WeaponMax = weapon.MaxDamage;
            s.AttackSpeedMs = weapon.SpeedMs;
        }
        else
        {
            s.WeaponMin = cls.Unarmed.MinDamage + cls.Unarmed.DamagePerLevel * levelsAbove1;
            s.WeaponMax = cls.Unarmed.MaxDamage + cls.Unarmed.DamagePerLevel * levelsAbove1;
            s.AttackSpeedMs = cls.Unarmed.SpeedMs;
        }
    }

    public static int PlayerMaxHealth(ClassDefinition cls, int level, UnitStats s) =>
        Math.Max(1, (int)(cls.BaseHealth + cls.HealthPerLevel * Math.Max(0, level - 1) + s.Stamina * 2f));

    public static int PlayerMaxPower(ClassDefinition cls, int level, UnitStats s) => cls.PowerType switch
    {
        PowerType.Mana => Math.Max(0, (int)(cls.BaseMana + cls.ManaPerLevel * Math.Max(0, level - 1) + s.Intellect * 5f)),
        PowerType.Rage => MaxRage,
        PowerType.Energy => MaxEnergy,
        _ => 0,
    };

    public static void ComputeCreature(UnitStats s, CreatureDefinition t, int level, AuraModifiers mods)
    {
        for (int i = 0; i <= (int)StatType.Spirit; i++) s.Values[i] = MathF.Max(0f, mods.Apply((StatType)i, 10f + level));
        s.Values[(int)StatType.Armor] = MathF.Max(0f, mods.Apply(StatType.Armor, 20f * level * t.Armor));
        s.Values[(int)StatType.AttackPower] = MathF.Max(0f, mods.Apply(StatType.AttackPower, 0f));
        s.Values[(int)StatType.SpellPower] = MathF.Max(0f, mods.Apply(StatType.SpellPower, 0f));
        s.CritChance = 5f;
        s.SpellCritChance = 5f;
        s.DodgeChance = 5f;
        s.ParryChance = 5f;
        float average = (2f + 1.5f * (level - 1)) * t.Damage;
        s.WeaponMin = average * 0.75f;
        s.WeaponMax = average * 1.25f;
        s.AttackSpeedMs = t.AttackSpeedMs;
    }

    public static int CreatureMaxHealth(CreatureDefinition t, int level) => Math.Max(1, (int)((35f + 15f * (level - 1)) * t.Health));

    public static int CreatureMaxMana(CreatureDefinition t, int level) => t.Mana > 0 ? (int)((60f + 30f * (level - 1)) * t.Mana) : 0;

    /// <summary>Physical damage reduction from armor (0..0.75), classic formula against the attacker's level.</summary>
    public static float ArmorReduction(int armor, int attackerLevel)
    {
        if (armor <= 0) return 0f;
        float reduction = armor / (armor + 400f + 85f * attackerLevel);
        return Math.Clamp(reduction, 0f, 0.75f);
    }

    /// <summary>Rage conversion value: dealing c damage gives 7.5 rage, taking c damage gives 2.5.</summary>
    public static float RageConversion(int level) => 0.0091107836f * level * level + 3.225598133f * level + 4.2652911f;

    public static void FillPlayerStats(Madlands.Shared.Net.PlayerStats msg, Unit unit)
    {
        var s = unit.Stats;
        for (int i = 0; i < UnitStats.Count; i++) msg.Stats[i] = (int)MathF.Round(s.Values[i]);
        msg.MeleeMinDamage = (int)MathF.Round(s.WeaponMin + s.AttackPowerBonus);
        msg.MeleeMaxDamage = (int)MathF.Round(s.WeaponMax + s.AttackPowerBonus);
        msg.MeleeSpeedMs = s.AttackSpeedMs;
        msg.CritChance = s.CritChance;
        msg.SpellCritChance = s.SpellCritChance;
        msg.DodgeChance = s.DodgeChance;
    }
}
