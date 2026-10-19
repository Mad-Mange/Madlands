using Madlands.Content;
using Madlands.Shared;
using Madlands.Shared.Movement;

namespace Madlands.Server.World;

/// <summary>A gatherable thing in the world (mushrooms): right-click gives its item, then it grows back after a while.</summary>
public sealed class WorldObject : Entity
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
}
