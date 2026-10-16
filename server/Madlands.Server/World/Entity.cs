using Madlands.Shared;
using Madlands.Shared.Movement;

namespace Madlands.Server.World;

/// <summary>Anything that exists in a map instance and can be seen by players.</summary>
public class Entity
{
    public Entity(EntityGuid guid, string name, uint displayId)
    {
        Guid = guid;
        Name = name;
        DisplayId = displayId;
    }

    public EntityGuid Guid { get; }
    public string Name { get; }
    public uint DisplayId { get; }

    public MovementState Movement;
    /// <summary>Server time when <see cref="Movement"/> was last set.</summary>
    public long MovementTimeMs;
    /// <summary>Server time of the last actual change; recent changes are repeated to observers.</summary>
    public long LastChangeMs;

    public MapInstance? Map { get; internal set; }
    internal long AoiCell;

    /// <summary>Players whose client currently has this entity spawned (kept in sync with their <see cref="Player.Known"/>).</summary>
    internal List<Player> Observers { get; } = new();

    public Vec3 Position => Movement.Position;

    public override string ToString() => $"{Name} ({Guid})";
}
