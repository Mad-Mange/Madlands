using Madlands.Server.Net;
using Madlands.Shared;
using Madlands.Shared.Movement;

namespace Madlands.Server.World;

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

    public Vec3 Position => Movement.Position;

    public override string ToString() => $"{Name} ({Guid})";
}

public sealed class Player : Entity
{
    public const uint DefaultDisplayId = 1;

    public Player(EntityGuid guid, string name, ClientSession session, MovementValidator validator)
        : base(guid, name, DefaultDisplayId)
    {
        Session = session;
        Validator = validator;
    }

    public ClientSession Session { get; }
    public MovementValidator Validator { get; }
    public ushort CorrectionId;

    /// <summary>Entities this player's client currently has spawned (its area of interest).</summary>
    public Dictionary<EntityGuid, Entity> Known { get; } = new();
}
