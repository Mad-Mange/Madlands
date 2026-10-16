using Madlands.Content;
using Madlands.Server.AI;
using Madlands.Server.Combat;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Movement;

namespace Madlands.Server.World;

/// <summary>A server-controlled unit spawned from a <see cref="CreatureDefinition"/>.</summary>
public sealed class Creature : Unit
{
    private readonly AuraModifiers _mods = new();

    public Creature(EntityGuid guid, CreatureDefinition template, SpawnDefinition? spawn, Vec3 home, float homeYaw, byte level)
        : base(guid, template.Name, template.DisplayId)
    {
        Template = template;
        Spawn = spawn;
        Home = home;
        HomeYaw = homeYaw;
        Level = level;
        Faction = (ushort)template.Faction;
        PowerType = template.Mana > 0 ? PowerType.Mana : PowerType.None;
        Movement = MovementState.Idle(home, homeYaw);
        Mover = new CreatureMover(this);
        Ai = new CreatureAI(this);
        RecalculateStats();
        Health = MaxHealth;
        Power = MaxPower;
    }

    public override bool IsPlayer => false;

    public CreatureDefinition Template { get; }
    /// <summary>The spawn it came from; null for temporary (GM-spawned) creatures, which never respawn.</summary>
    public SpawnDefinition? Spawn { get; }
    /// <summary>Where it stands when idle, and returns to after a fight.</summary>
    public Vec3 Home { get; }
    public float HomeYaw { get; }

    public ThreatList Threat { get; } = new();
    public CreatureMover Mover { get; }
    public CreatureAI Ai { get; }
    /// <summary>The first player to damage it; gets the kill credit.</summary>
    public EntityGuid Tapper;
    /// <summary>When the corpse disappears (dead creatures only).</summary>
    public long DespawnAtMs;

    public bool IsDummy => Template.Ai == CreatureAi.Dummy;

    public override void RecalculateStats()
    {
        _mods.Collect(Auras);
        StatFormulas.ComputeCreature(Stats, Template, Level, _mods);
        MaxHealth = StatFormulas.CreatureMaxHealth(Template, Level);
        MaxPower = StatFormulas.CreatureMaxMana(Template, Level);
        ApplyAuraModifiers(_mods);
    }
}
