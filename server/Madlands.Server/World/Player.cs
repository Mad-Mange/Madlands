using Madlands.Content;
using Madlands.Server.Combat;
using Madlands.Server.Items;
using Madlands.Server.Net;
using Madlands.Server.Quests;
using Madlands.Shared;
using Madlands.Shared.Game;

namespace Madlands.Server.World;

public sealed class Player : Unit
{
    private readonly AuraModifiers _mods = new();

    public Player(long characterId, string name, ClientSession session, MovementValidator validator, ClassDefinition cls, RaceDefinition race)
        : base(EntityGuid.Create(EntityType.Player, (ulong)characterId), name, race.DisplayId)
    {
        CharacterId = characterId;
        Session = session;
        Validator = validator;
        Class = cls;
        Race = race;
        ClassId = (byte)cls.Id;
        Faction = (ushort)race.Faction;
        PowerType = cls.PowerType;
    }

    public override bool IsPlayer => true;

    /// <summary>Database id; also the counter of the persistent <see cref="Entity.Guid"/>.</summary>
    public long CharacterId { get; }
    public ClientSession Session { get; }
    public MovementValidator Validator { get; }
    public ClassDefinition Class { get; }
    public RaceDefinition Race { get; }
    public int Xp;
    public ushort CorrectionId;
    public long NextSaveAtMs;

    /// <summary>Equipment, bags and money.</summary>
    public Inventory Inventory { get; } = new();
    public QuestLog Quests { get; } = new();
    /// <summary>The corpse whose loot window is open, if any.</summary>
    public Creature? Looting;
    /// <summary>The class's starting items were handed out (first login).</summary>
    public bool StarterKitGiven;
    public long NextExploreCheckMs;

    /// <summary>Entities this player's client currently has spawned (its area of interest).</summary>
    public Dictionary<EntityGuid, Entity> Known { get; } = new();

    /// <summary>Spells learned from the class at the current level.</summary>
    public HashSet<uint> KnownSpells { get; } = new();
    /// <summary>The private <see cref="Madlands.Shared.Net.PlayerStats"/> must be resent.</summary>
    public bool StatsDirty = true;
    /// <summary>Creatures that have this player on their threat list (keeps the player in combat).</summary>
    public HashSet<Creature> ThreatenedBy { get; } = new();
    /// <summary>Throttles repeated auto-attack error messages.</summary>
    public long NextAttackErrorMs;

    public int GmLevel;
    /// <summary>GM: takes no damage and pays no costs.</summary>
    public bool GodMode;
    /// <summary>GM: extra movement speed factor (1 = normal).</summary>
    public float GmSpeed = 1f;

    public bool IsGm => GmLevel > 0;

    public override void RecalculateStats()
    {
        _mods.Collect(Auras);
        StatFormulas.ComputePlayer(Stats, Class, Race, Level, _mods, Inventory);
        MaxHealth = StatFormulas.PlayerMaxHealth(Class, Level, Stats);
        MaxPower = StatFormulas.PlayerMaxPower(Class, Level, Stats);
        ApplyAuraModifiers(_mods);
        MainHandDisplay = Inventory.Equipped(EquipSlot.MainHand)?.Item.DisplayId ?? 0;
        OffHandDisplay = Inventory.Equipped(EquipSlot.OffHand)?.Item.DisplayId ?? 0;
        for (int i = 0; i < VisibleGear.Count; i++)
            SetGear(i, Inventory.Equipped(VisibleGear.Slots[i])?.Item.DisplayId ?? 0);
        if (GmSpeed != 1f) SpeedPercent = (ushort)Math.Clamp(SpeedPercent * GmSpeed, 10, 1000);
        StatsDirty = true;
    }

    /// <summary>Refreshes <see cref="KnownSpells"/> for the current level; true if the set changed.</summary>
    public bool UpdateKnownSpells()
    {
        var wanted = Class.Spells.Where(cs => cs.Level <= Level).Select(cs => cs.Spell).ToHashSet();
        if (wanted.SetEquals(KnownSpells)) return false;
        KnownSpells.Clear();
        KnownSpells.UnionWith(wanted);
        return true;
    }
}
