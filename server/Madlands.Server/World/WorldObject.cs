using Madlands.Content;
using Madlands.Server.Items;
using Madlands.Shared;
using Madlands.Shared.Movement;

namespace Madlands.Server.World;

/// <summary>
/// A thing in the world players use with a right-click: a gathered object (mushrooms) gives its item, a chest opens
/// a loot window. Either way it disappears and comes back after a while.
/// </summary>
public sealed class WorldObject : Entity, ILootSource
{
    public WorldObject(EntityGuid guid, GameObjectDefinition template, ObjectSpawnDefinition spawn, Vec3 position)
        : base(guid, template.Name, template.DisplayId)
    {
        Template = template;
        Spawn = spawn;
        Movement = MovementState.Idle(position, spawn.YawDegrees * MathUtil.Deg2Rad);
    }

    public GameObjectDefinition Template { get; }
    public ObjectSpawnDefinition Spawn { get; }

    /// <summary>A chest's contents, rolled when it is first opened; null until then.</summary>
    public Loot? Loot { get; set; }
    /// <summary>The player with the chest's loot window open.</summary>
    public Player? Looter { get; set; }
    /// <summary>When the chest was first opened (it does not stay forever with leftovers in it).</summary>
    public long OpenedAtMs;
}
