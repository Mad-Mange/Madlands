using Madlands.Content;
using Madlands.Navigation;
using Madlands.Shared;
using Serilog;

namespace Madlands.Server.World;

/// <summary>
/// Owns all map instances and hands out entity ids. Each map's navmesh and collision geometry are
/// built once at startup and shared (read-only) by all instances of that map.
/// </summary>
public sealed class WorldManager
{
    /// <summary>The persistent open-world copy of a map. Dungeons/layers will use higher ids.</summary>
    public const uint OpenWorldInstance = 1;

    private static readonly ILogger Log = Serilog.Log.ForContext<WorldManager>();

    private readonly Dictionary<(ushort MapId, uint InstanceId), MapInstance> _instances = new();
    private long _nextCreatureCounter;
    private long _nextObjectCounter;

    public WorldManager(ContentDatabase content, int? randomSeed = null)
    {
        Content = content;
        foreach (var map in content.Maps.Values)
        {
            var nav = NavMesh.Build(map.HeightMap, map.Collision);
            var collision = new CollisionWorld(map.HeightMap, map.Collision);
            Log.Information("Map {MapId} '{Name}': navmesh {Tiles} tiles / {Polys} polys built in {Ms:0} ms, {Triangles} obstacle triangles, {Spawns} creature spawns",
                map.Id, map.Definition.Name, nav.TileCount, nav.PolyCount, nav.BuildTime.TotalMilliseconds, map.Collision.TriangleCount, map.Spawns.Count);
            _instances.Add((map.Id, OpenWorldInstance), new MapInstance(map, OpenWorldInstance, this, nav, collision, randomSeed));
        }
    }

    public ContentDatabase Content { get; }
    public IEnumerable<MapInstance> Instances => _instances.Values;
    public int PlayerCount => _instances.Values.Sum(i => i.PlayerCount);
    public int EntityCount => _instances.Values.Sum(i => i.EntityCount);

    public MapInstance? GetInstance(ushort mapId, uint instanceId) =>
        _instances.TryGetValue((mapId, instanceId), out var instance) ? instance : null;

    /// <summary>Creature ids are unique per realm process (not persistent). Thread-safe for future per-instance threads.</summary>
    public EntityGuid NextCreatureGuid() => EntityGuid.Create(EntityType.Creature, (ulong)Interlocked.Increment(ref _nextCreatureCounter));

    /// <summary>Gatherable object ids, like creature ids.</summary>
    public EntityGuid NextObjectGuid() => EntityGuid.Create(EntityType.GameObject, (ulong)Interlocked.Increment(ref _nextObjectCounter));

    public void Tick(long nowMs, long tick)
    {
        foreach (var instance in _instances.Values) instance.Tick(nowMs, tick);
    }
}
