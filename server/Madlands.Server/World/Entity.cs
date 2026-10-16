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

    public Player(long characterId, string name, ClientSession session, MovementValidator validator)
        : base(EntityGuid.Create(EntityType.Player, (ulong)characterId), name, DefaultDisplayId)
    {
        CharacterId = characterId;
        Session = session;
        Validator = validator;
    }

    /// <summary>Database id; also the counter of the persistent <see cref="Entity.Guid"/>.</summary>
    public long CharacterId { get; }
    public ClientSession Session { get; }
    public MovementValidator Validator { get; }
    public byte Level { get; set; } = 1;
    public ushort CorrectionId;
    public long NextSaveAtMs;

    /// <summary>Entities this player's client currently has spawned (its area of interest).</summary>
    public Dictionary<EntityGuid, Entity> Known { get; } = new();
}
