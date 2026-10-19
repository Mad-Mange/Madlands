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
    /// <summary>Gatherable objects (objects.json) relative to the map folder; null = none.</summary>
    public string? Objects { get; set; }
    public SpawnPoint Spawn { get; set; } = new();
    /// <summary>Villages: characters build up rested experience here (fast, logged in or out).</summary>
    public List<RestAreaDefinition> RestAreas { get; set; } = new();

    public bool IsRestArea(float x, float z)
    {
        foreach (var area in RestAreas)
        {
            float dx = x - area.X, dz = z - area.Z;
            if (dx * dx + dz * dz <= area.Radius * area.Radius) return true;
        }
        return false;
    }
}

public sealed class RestAreaDefinition
{
    public string Name { get; set; } = "";
    public float X { get; set; }
    public float Z { get; set; }
    public float Radius { get; set; }
}

public sealed class SpawnPoint
{
    public float X { get; set; }
    public float Z { get; set; }
    public float YawDegrees { get; set; }
}

public sealed class LoadedMap
{
    public LoadedMap(MapDefinition definition, HeightMap heightMap, CollisionMesh? collision = null, IReadOnlyList<SpawnDefinition>? spawns = null,
        IReadOnlyList<ObjectSpawnDefinition>? objects = null)
    {
        Definition = definition;
        HeightMap = heightMap;
        Collision = collision ?? CollisionMesh.Empty;
        Spawns = spawns ?? Array.Empty<SpawnDefinition>();
        Objects = objects ?? Array.Empty<ObjectSpawnDefinition>();
    }

    public MapDefinition Definition { get; }
    public HeightMap HeightMap { get; }
    public CollisionMesh Collision { get; }
    public IReadOnlyList<SpawnDefinition> Spawns { get; }
    public IReadOnlyList<ObjectSpawnDefinition> Objects { get; }
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
    /// <summary>The race's character model (client art).</summary>
    public uint DisplayId { get; set; } = 1;
    /// <summary>Added to the class's attributes.</summary>
    public StatBlock Stats { get; set; } = new();
}

/// <summary>Bare-handed damage: used when nothing is equipped in the main hand.</summary>
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
    /// <summary>Fists: the damage when no weapon is equipped.</summary>
    public WeaponDefinition Unarmed { get; set; } = new();
    /// <summary>Spells learned automatically at a level.</summary>
    public List<ClassSpell> Spells { get; set; } = new();
    /// <summary>Given once, on the character's first login: equipped where possible, the rest into the bags.</summary>
    public List<ItemCount> StartingItems { get; set; } = new();
}

public sealed class ItemCount
{
    public uint Item { get; set; }
    public int Count { get; set; } = 1;
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
    /// <summary>Amount (+perLevel) of a power every tick (drinking restores mana).</summary>
    PeriodicEnergize,
}

public sealed class AuraEffectDefinition
{
    public AuraEffectType Type { get; set; }
    public float Amount { get; set; }
    public float PerLevel { get; set; }
    public StatType? Stat { get; set; }
    public bool Percent { get; set; }
    /// <summary>PeriodicEnergize: which power.</summary>
    public PowerType Power { get; set; } = PowerType.None;
}

public sealed class AuraDefinition
{
    public int DurationMs { get; set; }
    /// <summary>Interval of periodic effects.</summary>
    public int TickMs { get; set; } = 3000;
    public int MaxStacks { get; set; } = 1;
    /// <summary>Removed when the unit enters combat (eating and drinking).</summary>
    public bool BreakOnCombat { get; set; }
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
    /// <summary>Only used below this much health, in percent (a boss's enrage); 0 = at any health.</summary>
    public float HealthBelow { get; set; }
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
    /// <summary>Loot table id (content/loot.json); 0 = drops nothing.</summary>
    public int Loot { get; set; }
    /// <summary>What it says when spoken to; empty = nothing (quest givers and vendors still talk business).</summary>
    public string Gossip { get; set; } = "";
    /// <summary>Item ids it sells; empty = not a vendor.</summary>
    public List<uint> Vendor { get; set; } = new();
    /// <summary>What the NPC is for players: its symbol over the head and on the maps, and its title.</summary>
    public NpcRole Role { get; set; }
    /// <summary>Items it holds (only their looks matter): a weapon and a shield or held item.</summary>
    public CreatureEquipment Equipment { get; set; } = new();
}

public sealed class CreatureEquipment
{
    public uint MainHand { get; set; }
    public uint OffHand { get; set; }
}

/// <summary>Stats an item adds, flat (armor too).</summary>
public sealed class ItemStatBlock
{
    public int Strength { get; set; }
    public int Agility { get; set; }
    public int Stamina { get; set; }
    public int Intellect { get; set; }
    public int Spirit { get; set; }
    public int Armor { get; set; }
    public int AttackPower { get; set; }
    public int SpellPower { get; set; }

    public int this[StatType stat] => stat switch
    {
        StatType.Strength => Strength,
        StatType.Agility => Agility,
        StatType.Stamina => Stamina,
        StatType.Intellect => Intellect,
        StatType.Spirit => Spirit,
        StatType.Armor => Armor,
        StatType.AttackPower => AttackPower,
        StatType.SpellPower => SpellPower,
        _ => 0,
    };
}

public sealed class ItemWeaponDefinition
{
    public int MinDamage { get; set; }
    public int MaxDamage { get; set; }
    public int SpeedMs { get; set; } = 2000;
}

/// <summary>content/items.json entry (ADR 0010).</summary>
public sealed class ItemDefinition
{
    public uint Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public uint Icon { get; set; }
    /// <summary>How it looks equipped (weapons in hand); 0 = nothing to show.</summary>
    public uint DisplayId { get; set; }
    public ItemQuality Quality { get; set; } = ItemQuality.Common;
    public ItemClass Class { get; set; } = ItemClass.Junk;
    /// <summary>Where it is equipped; none = not equippable.</summary>
    public InventoryType Slot { get; set; } = InventoryType.None;
    public int RequiredLevel { get; set; }
    /// <summary>Class ids that may equip or use it; empty = everyone.</summary>
    public List<int> Classes { get; set; } = new();
    public int StackSize { get; set; } = 1;
    /// <summary>Copper a vendor charges.</summary>
    public int BuyPrice { get; set; }
    /// <summary>Copper a vendor pays; 0 = cannot be sold.</summary>
    public int SellPrice { get; set; }
    public ItemStatBlock Stats { get; set; } = new();
    /// <summary>Weapons (mainHand and twoHand) only.</summary>
    public ItemWeaponDefinition? Weapon { get; set; }
    /// <summary>Spell cast when used (instant); 0 = not usable.</summary>
    public uint UseSpell { get; set; }
    /// <summary>Containers: how many slots.</summary>
    public int BagSlots { get; set; }

    public bool IsEquippable => Slot != InventoryType.None;
    public bool IsBag => Slot == InventoryType.Bag;
    public bool AllowsClass(int classId) => Classes.Count == 0 || Classes.Contains(classId);
    public override string ToString() => $"{Name} ({Id})";
}

public sealed class LootMoney
{
    public int Min { get; set; }
    public int Max { get; set; }
}

public sealed class LootItemDefinition
{
    public uint Item { get; set; }
    /// <summary>0..1. Quest items only drop for players who still need them for a quest.</summary>
    public float Chance { get; set; } = 1f;
    public int Min { get; set; } = 1;
    public int Max { get; set; } = 1;
}

/// <summary>Rolled once: with <see cref="Chance"/> exactly one of the items drops (each equally likely).</summary>
public sealed class LootGroupDefinition
{
    public float Chance { get; set; }
    public List<uint> Items { get; set; } = new();
}

/// <summary>content/loot.json entry: what a creature drops. Creatures refer to it by id.</summary>
public sealed class LootTableDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Copper, rolled uniformly in min..max.</summary>
    public LootMoney? Money { get; set; }
    public List<LootItemDefinition> Items { get; set; } = new();
    public List<LootGroupDefinition> Groups { get; set; } = new();
}

public sealed class QuestObjectiveDefinition
{
    public ObjectiveType Type { get; set; }
    /// <summary>Kill and talk: the creature id.</summary>
    public int Creature { get; set; }
    /// <summary>Collect: the item id.</summary>
    public uint Item { get; set; }
    public int Count { get; set; } = 1;
    /// <summary>Explore: the place (map, center, radius in meters).</summary>
    public int Map { get; set; }
    public float X { get; set; }
    public float Z { get; set; }
    public float Radius { get; set; }
    /// <summary>Quest log line; generated from the target's name when empty (explore needs one).</summary>
    public string Text { get; set; } = "";
}

public sealed class QuestRewardDefinition
{
    public int Xp { get; set; }
    /// <summary>Copper.</summary>
    public int Money { get; set; }
    public List<ItemCount> Items { get; set; } = new();
    /// <summary>The player picks one.</summary>
    public List<ItemCount> Choices { get; set; } = new();
}

/// <summary>content/quests.json entry (ADR 0011).</summary>
public sealed class QuestDefinition
{
    public uint Id { get; set; }
    public string Title { get; set; } = "";
    /// <summary>The level it is meant for (experience falls off for players far above it).</summary>
    public int Level { get; set; } = 1;
    /// <summary>The lowest level that can take it.</summary>
    public int MinLevel { get; set; } = 1;
    /// <summary>Creature that offers it.</summary>
    public int Giver { get; set; }
    /// <summary>Creature that takes it back.</summary>
    public int Ender { get; set; }
    /// <summary>Quests that must be turned in first.</summary>
    public List<uint> Requires { get; set; } = new();
    /// <summary>Class ids that may take it; empty = everyone.</summary>
    public List<int> Classes { get; set; } = new();
    public string Details { get; set; } = "";
    /// <summary>One-line summary of what to do.</summary>
    public string Summary { get; set; } = "";
    public string Progress { get; set; } = "";
    public string Completion { get; set; } = "";
    public List<QuestObjectiveDefinition> Objectives { get; set; } = new();
    public QuestRewardDefinition Rewards { get; set; } = new();

    public bool AllowsClass(int classId) => Classes.Count == 0 || Classes.Contains(classId);
    public override string ToString() => $"{Title} ({Id})";
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

/// <summary>maps/&lt;id&gt;/objects.json entry: where a gatherable object grows (written by Unity's Export Map).</summary>
public sealed class ObjectSpawnDefinition
{
    public int Object { get; set; }
    public float X { get; set; }
    public float Z { get; set; }
    public float YawDegrees { get; set; }
}

/// <summary>
/// content/objects.json entry: something in the world players gather with a right-click (mushrooms, herbs). It
/// gives an item and grows back after a while.
/// </summary>
public sealed class GameObjectDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>How it looks (the client's object displays).</summary>
    public uint DisplayId { get; set; }
    /// <summary>The item it gives, MinCount..MaxCount of them.</summary>
    public uint Item { get; set; }
    public int MinCount { get; set; } = 1;
    public int MaxCount { get; set; } = 1;
    /// <summary>Grows back this long after it was gathered.</summary>
    public int RespawnSeconds { get; set; } = 60;
    /// <summary>Only players who still need the item for a quest can gather it (and see it shine).</summary>
    public bool QuestOnly { get; set; } = true;
}

/// <summary>content/levels.json</summary>
public sealed class LevelTable
{
    public int MaxLevel { get; set; } = 10;
    /// <summary>Experience from level i+1 to i+2 (index 0 = level 1 to 2).</summary>
    public List<int> XpToNextLevel { get; set; } = new();

    /// <summary>0 at the level cap.</summary>
    public int XpToNext(int level) => level >= 1 && level < MaxLevel && level - 1 < XpToNextLevel.Count ? XpToNextLevel[level - 1] : 0;
    public RestRules Rest { get; set; } = new();
}

/// <summary>Rested experience (levels.json "rest"); percentages are of the current level's experience bar.</summary>
public sealed class RestRules
{
    /// <summary>Built up per hour in a rest area, logged in or out.</summary>
    public float PercentPerHour { get; set; } = 2.5f;
    /// <summary>Logged out outside a rest area, rest builds up this much slower (0.25 = a quarter of the speed).</summary>
    public float ElsewhereFactor { get; set; } = 0.25f;
    /// <summary>The pool never grows past this.</summary>
    public float MaxPercent { get; set; } = 150f;
}
