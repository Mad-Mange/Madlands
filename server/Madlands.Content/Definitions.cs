using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Terrain;

namespace Madlands.Content;

// Content definitions, deserialized from content/*.json (camelCase, enums as camelCase strings).
// Ids are stable and never reused: they are stored on characters and sent to clients.

/// <summary>content/maps/&lt;id&gt;/map.json (written by Unity: Madlands > Export Map).</summary>
public sealed class MapDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Height map file relative to the map folder.</summary>
    public string Heightmap { get; set; } = "height.bin";
    /// <summary>Static obstacle triangles relative to the map folder; null = no obstacles.</summary>
    public string? Collision { get; set; }
    /// <summary>Creature spawn list relative to the map folder; null = no creatures.</summary>
    public string? Spawns { get; set; }
    public SpawnPoint Spawn { get; set; } = new();
}

public sealed class SpawnPoint
{
    public float X { get; set; }
    public float Z { get; set; }
    public float YawDegrees { get; set; }
}

public sealed class LoadedMap
{
    public LoadedMap(MapDefinition definition, HeightMap heightMap, CollisionMesh? collision = null, IReadOnlyList<SpawnDefinition>? spawns = null)
    {
        Definition = definition;
        HeightMap = heightMap;
        Collision = collision ?? CollisionMesh.Empty;
        Spawns = spawns ?? Array.Empty<SpawnDefinition>();
    }

    public MapDefinition Definition { get; }
    public HeightMap HeightMap { get; }
    public CollisionMesh Collision { get; }
    public IReadOnlyList<SpawnDefinition> Spawns { get; }
    public ushort Id => (ushort)Definition.Id;

    public Vec3 SpawnPosition =>
        new(Definition.Spawn.X, HeightMap.GetHeight(Definition.Spawn.X, Definition.Spawn.Z), Definition.Spawn.Z);

    public float SpawnYaw => Definition.Spawn.YawDegrees * MathUtil.Deg2Rad;
}

/// <summary>Primary attributes. Used for class base values, per-level growth and racial modifiers.</summary>
public sealed class StatBlock
{
    public float Strength { get; set; }
    public float Agility { get; set; }
    public float Stamina { get; set; }
    public float Intellect { get; set; }
    public float Spirit { get; set; }

    public float this[StatType stat] => stat switch
    {
        StatType.Strength => Strength,
        StatType.Agility => Agility,
        StatType.Stamina => Stamina,
        StatType.Intellect => Intellect,
        StatType.Spirit => Spirit,
        _ => 0f,
    };
}

/// <summary>content/races.json entry.</summary>
public sealed class RaceDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Map new characters of this race start on (at the map's spawn point).</summary>
    public int StartMap { get; set; } = 1;
    public int Faction { get; set; } = 1;
    /// <summary>Added to the class's attributes.</summary>
    public StatBlock Stats { get; set; } = new();
}

/// <summary>Until items exist (Phase 4) every class fights with this built-in weapon.</summary>
public sealed class WeaponDefinition
{
    public float MinDamage { get; set; } = 1;
    public float MaxDamage { get; set; } = 2;
    /// <summary>Added to both min and max per level above 1.</summary>
    public float DamagePerLevel { get; set; }
    public int SpeedMs { get; set; } = 2000;
}

public sealed class ClassSpell
{
    public uint Spell { get; set; }
    public int Level { get; set; } = 1;
}

/// <summary>content/classes.json entry.</summary>
public sealed class ClassDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public PowerType PowerType { get; set; } = PowerType.Mana;
    public StatBlock BaseStats { get; set; } = new();
    public StatBlock StatsPerLevel { get; set; } = new();
    public float BaseHealth { get; set; } = 20;
    public float HealthPerLevel { get; set; } = 10;
    public float BaseMana { get; set; }
    public float ManaPerLevel { get; set; }
    public WeaponDefinition Weapon { get; set; } = new();
    /// <summary>Spells learned automatically at a level.</summary>
    public List<ClassSpell> Spells { get; set; } = new();
}

/// <summary>content/factions.json entry. See <see cref="FactionTable"/> for the reaction rules.</summary>
public sealed class FactionDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<int> Friendly { get; set; } = new();
    public List<int> Hostile { get; set; } = new();
}

public enum SpellEffectType
{
    /// <summary>Min..max (+perLevel per caster level) damage of the spell's school, plus coefficient x spell power.</summary>
    SchoolDamage,
    /// <summary>A weapon swing: percent of weapon damage plus bonus (+bonusPerLevel). Can be dodged and parried.</summary>
    WeaponDamage,
    Heal,
    /// <summary>Applies the spell's aura.</summary>
    ApplyAura,
    /// <summary>Restores min..max of the effect's power type.</summary>
    Energize,
}

public enum EffectTargets
{
    /// <summary>The spell's target (or the caster for self spells).</summary>
    Target,
    Caster,
    EnemiesNearCaster,
    EnemiesNearTarget,
    AlliesNearCaster,
}

public sealed class SpellEffectDefinition
{
    public SpellEffectType Type { get; set; }
    public EffectTargets Targets { get; set; } = EffectTargets.Target;
    /// <summary>Radius for the area target types (meters).</summary>
    public float Radius { get; set; }
    public float Min { get; set; }
    public float Max { get; set; }
    /// <summary>Added to min and max per caster level above 1.</summary>
    public float PerLevel { get; set; }
    /// <summary>Share of attack power (physical) or spell power added to the amount.</summary>
    public float Coefficient { get; set; }
    /// <summary>WeaponDamage: percent of a normal swing.</summary>
    public float Percent { get; set; } = 100;
    /// <summary>WeaponDamage: flat bonus.</summary>
    public float Bonus { get; set; }
    public float BonusPerLevel { get; set; }
    /// <summary>Energize: which power.</summary>
    public PowerType Power { get; set; } = PowerType.None;
}

public enum AuraEffectType
{
    /// <summary>Amount (+perLevel) damage of the spell's school every tick.</summary>
    PeriodicDamage,
    PeriodicHeal,
    /// <summary>Changes a stat by amount, flat or in percent.</summary>
    ModStat,
    /// <summary>Movement speed change in percent (negative = slow). The strongest slow and the strongest boost apply.</summary>
    ModSpeed,
    /// <summary>Damage done change in percent.</summary>
    ModDamageDone,
    Stun,
    Root,
    /// <summary>Absorbs amount (+perLevel) damage, then breaks.</summary>
    Absorb,
}

public sealed class AuraEffectDefinition
{
    public AuraEffectType Type { get; set; }
    public float Amount { get; set; }
    public float PerLevel { get; set; }
    public StatType? Stat { get; set; }
    public bool Percent { get; set; }
}

public sealed class AuraDefinition
{
    public int DurationMs { get; set; }
    /// <summary>Interval of periodic effects.</summary>
    public int TickMs { get; set; } = 3000;
    public int MaxStacks { get; set; } = 1;
    public List<AuraEffectDefinition> Effects { get; set; } = new();
}

/// <summary>content/spells.json entry (ADR 0005: spells and auras are lists of effects).</summary>
public sealed class SpellDefinition
{
    public uint Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public uint Icon { get; set; }
    public SpellSchool School { get; set; } = SpellSchool.Physical;
    public SpellTarget Target { get; set; } = SpellTarget.Enemy;
    public float RangeMin { get; set; }
    public float RangeMax { get; set; } = GameConstants.MeleeRange;
    public int CastTimeMs { get; set; }
    public int CooldownMs { get; set; }
    public int GcdMs { get; set; } = GameConstants.DefaultGcdMs;
    public PowerType PowerType { get; set; } = PowerType.None;
    public int Cost { get; set; }
    /// <summary>Meters per second; 0 = instant hit.</summary>
    public float ProjectileSpeed { get; set; }
    public List<SpellFlags> Flags { get; set; } = new();
    public List<SpellEffectDefinition> Effects { get; set; } = new();
    public AuraDefinition? Aura { get; set; }

    public SpellFlags AllFlags => Flags.Aggregate(SpellFlags.None, (a, f) => a | f);
    public bool Has(SpellFlags flag) => (AllFlags & flag) != 0;
}

public enum CreatureAi
{
    /// <summary>Chases its target and fights in melee, using its spells when they come up.</summary>
    Melee,
    /// <summary>Stays at spell range and casts; falls back to melee when it cannot cast.</summary>
    Caster,
    /// <summary>Never moves or fights back, never dies (health stops at 1). For practising.</summary>
    Dummy,
}

public sealed class CreatureSpell
{
    public uint Spell { get; set; }
    /// <summary>Chance to use it each time the creature decides what to do (0..1).</summary>
    public float Chance { get; set; } = 1f;
    public int CooldownMs { get; set; } = 8000;
}

/// <summary>content/creatures.json entry: a creature template; spawns place instances of it.</summary>
public sealed class CreatureDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public uint DisplayId { get; set; }
    public int MinLevel { get; set; } = 1;
    public int MaxLevel { get; set; } = 1;
    public int Faction { get; set; }
    public CreatureAi Ai { get; set; } = CreatureAi.Melee;
    /// <summary>Hostile players within this radius are attacked; 0 = only fights back.</summary>
    public float AggroRadius { get; set; }
    public float RunSpeed { get; set; } = GameConstants.RunSpeed;
    public float WalkSpeed { get; set; } = GameConstants.WalkSpeed;
    /// <summary>Multipliers on the level-based creature stats.</summary>
    public float Health { get; set; } = 1f;
    public float Damage { get; set; } = 1f;
    public float Armor { get; set; } = 1f;
    /// <summary>Multiplier on the level-based creature mana; 0 = no mana.</summary>
    public float Mana { get; set; }
    public int AttackSpeedMs { get; set; } = 2000;
    public List<CreatureSpell> Spells { get; set; } = new();
    public int RespawnSeconds { get; set; } = 60;
    public int CorpseSeconds { get; set; } = 20;
    /// <summary>Multiplier on the experience it gives.</summary>
    public float Xp { get; set; } = 1f;
    /// <summary>Same-faction creatures within this radius join a fight.</summary>
    public float CallForHelpRadius { get; set; } = 6f;
}

/// <summary>content/maps/&lt;id&gt;/spawns.json entry (written by Unity from CreatureSpawnPoint markers).</summary>
public sealed class SpawnDefinition
{
    public int Creature { get; set; }
    public float X { get; set; }
    public float Z { get; set; }
    public float YawDegrees { get; set; }
    /// <summary>Wanders randomly within this radius of the spawn point; 0 = stands still.</summary>
    public float WanderRadius { get; set; }
    /// <summary>0 = the creature template's respawn time.</summary>
    public int RespawnSeconds { get; set; }
}

/// <summary>content/levels.json</summary>
public sealed class LevelTable
{
    public int MaxLevel { get; set; } = 10;
    /// <summary>Experience from level i+1 to i+2 (index 0 = level 1 to 2).</summary>
    public List<int> XpToNextLevel { get; set; } = new();

    /// <summary>0 at the level cap.</summary>
    public int XpToNext(int level) => level >= 1 && level < MaxLevel && level - 1 < XpToNextLevel.Count ? XpToNextLevel[level - 1] : 0;
}
