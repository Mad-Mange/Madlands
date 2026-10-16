using LiteNetLib;
using Madlands.Content;
using Madlands.Shared;
using Madlands.Shared.Movement;
using Madlands.Shared.Net;
using Madlands.Shared.Terrain;
using Serilog;

namespace Madlands.Server.World;

/// <summary>
/// One running copy of a map: the open world (instance 1), and later dungeon instances or
/// overflow "layers" of the same map. An instance owns its entities and shares no mutable state
/// with other instances, so instances can later run on their own threads or processes.
/// </summary>
public sealed class MapInstance
{
    public const float ViewRadius = 150f;
    public const float DespawnHysteresis = 20f;
    /// <summary>Changed states are repeated to observers this long, covering lost unreliable packets.</summary>
    public const long RecentChangeWindowMs = 1000;
    /// <summary>Visibility and movement broadcasts run every N ticks (20 Hz / 2 = 10 Hz).</summary>
    public const int BroadcastEveryTicks = 2;

    private static readonly ILogger Log = Serilog.Log.ForContext<MapInstance>();

    private readonly AoiGrid _grid = new();
    private readonly Dictionary<EntityGuid, Entity> _entities = new();
    private readonly List<Player> _players = new();
    private readonly List<Entity> _queryScratch = new();
    private readonly List<EntityGuid> _removeScratch = new();
    private readonly PacketWriter _writer = new(1500);
    private readonly EntitySpawn _spawnMsg = new();
    private readonly EntityDespawn _despawnMsg = new();
    private readonly EntityMoves _movesMsg = new();
    private readonly MoveCorrection _correctionMsg = new();

    public MapInstance(LoadedMap map, uint instanceId)
    {
        Map = map;
        InstanceId = instanceId;
    }

    public LoadedMap Map { get; }
    public ushort MapId => Map.Id;
    public uint InstanceId { get; }
    public HeightMap HeightMap => Map.HeightMap;
    public int EntityCount => _entities.Count;
    /// <summary>Loop thread only.</summary>
    public IEnumerable<Entity> Entities => _entities.Values;
    public int PlayerCount => _players.Count;
    public long CorrectionsSent { get; private set; }

    public void AddPlayer(Player player, long nowMs)
    {
        player.Map = this;
        player.MovementTimeMs = nowMs;
        player.LastChangeMs = nowMs;
        player.Validator.Reset(player.Movement, nowMs);
        _entities.Add(player.Guid, player);
        _players.Add(player);
        _grid.Add(player);
    }

    public void RemoveEntity(Entity entity)
    {
        if (!_entities.Remove(entity.Guid)) return;
        _grid.Remove(entity);
        if (entity is Player player) _players.Remove(player);
        foreach (var observer in _players)
        {
            if (observer.Known.Remove(entity.Guid)) SendDespawn(observer, entity.Guid);
        }
        entity.Map = null;
    }

    public void HandleMove(Player player, MoveUpdate update, long nowMs)
    {
        // Updates sent before the client applied our latest correction are meaningless now.
        if (update.CorrectionAck != player.CorrectionId) return;

        switch (player.Validator.Validate(update.State, update.ClientTimeMs, nowMs))
        {
            case MoveVerdict.Accepted:
                if (!update.State.Equals(player.Movement)) player.LastChangeMs = nowMs;
                player.Movement = update.State;
                player.MovementTimeMs = nowMs;
                _grid.Move(player);
                break;
            case MoveVerdict.Rejected:
                Correct(player, nowMs, player.Validator.LastRejectReason);
                break;
            case MoveVerdict.Stale:
                break;
        }
    }

    public void Tick(long nowMs, long tick)
    {
        if (tick % BroadcastEveryTicks != 0) return;
        foreach (var player in _players)
        {
            UpdateVisibility(player, nowMs);
            SendMoves(player, nowMs);
        }
    }

    private void Correct(Player player, long nowMs, string reason)
    {
        var state = player.Validator.LastAccepted;
        state.Velocity = Vec3.Zero;
        state.Flags = MovementFlags.None;
        state.Position = state.Position.WithY(Math.Max(state.Position.Y, HeightMap.GetHeight(state.Position.X, state.Position.Z)));

        player.CorrectionId++;
        player.Movement = state;
        player.MovementTimeMs = nowMs;
        player.LastChangeMs = nowMs;
        player.Validator.Reset(state, nowMs);
        _grid.Move(player);
        CorrectionsSent++;

        _correctionMsg.CorrectionId = player.CorrectionId;
        _correctionMsg.State = state;
        player.Session.Send(_writer, _correctionMsg, DeliveryMethod.ReliableOrdered);
        Log.Debug("Corrected {Player}: {Reason}", player, reason);
    }

    private void UpdateVisibility(Player player, long nowMs)
    {
        _queryScratch.Clear();
        _grid.Query(player.Position, ViewRadius, _queryScratch);
        foreach (var entity in _queryScratch)
        {
            if (entity == player) continue;
            if (player.Known.TryAdd(entity.Guid, entity)) SendSpawn(player, entity, nowMs);
        }

        _removeScratch.Clear();
        float maxSq = (ViewRadius + DespawnHysteresis) * (ViewRadius + DespawnHysteresis);
        foreach (var (guid, entity) in player.Known)
        {
            if (Vec3.HorizontalDistanceSquared(entity.Position, player.Position) > maxSq) _removeScratch.Add(guid);
        }
        foreach (var guid in _removeScratch)
        {
            player.Known.Remove(guid);
            SendDespawn(player, guid);
        }
    }

    private void SendMoves(Player player, long nowMs)
    {
        int maxEntries = EntityMoves.MaxEntriesFor(player.Session.MaxUnreliablePacketSize);
        _movesMsg.ServerTimeMs = nowMs;
        _movesMsg.Entries.Clear();
        foreach (var entity in player.Known.Values)
        {
            if (nowMs - entity.LastChangeMs > RecentChangeWindowMs) continue;
            _movesMsg.Entries.Add(new EntityMoveEntry
            {
                Guid = entity.Guid,
                AgeMs = (ushort)Math.Min(nowMs - entity.MovementTimeMs, ushort.MaxValue),
                State = entity.Movement,
            });
            if (_movesMsg.Entries.Count == maxEntries)
            {
                player.Session.Send(_writer, _movesMsg, DeliveryMethod.Unreliable);
                _movesMsg.Entries.Clear();
            }
        }
        if (_movesMsg.Entries.Count > 0) player.Session.Send(_writer, _movesMsg, DeliveryMethod.Unreliable);
    }

    private void SendSpawn(Player observer, Entity entity, long nowMs)
    {
        _spawnMsg.Guid = entity.Guid;
        _spawnMsg.Name = entity.Name;
        _spawnMsg.DisplayId = entity.DisplayId;
        _spawnMsg.StateServerTimeMs = entity.MovementTimeMs;
        _spawnMsg.State = entity.Movement;
        observer.Session.Send(_writer, _spawnMsg, DeliveryMethod.ReliableOrdered);
    }

    private void SendDespawn(Player observer, EntityGuid guid)
    {
        _despawnMsg.Guid = guid;
        observer.Session.Send(_writer, _despawnMsg, DeliveryMethod.ReliableOrdered);
    }
}
