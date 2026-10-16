using Madlands.Content;
using Madlands.Shared;

namespace Madlands.Server.World;

/// <summary>Owns all map instances and hands out entity ids.</summary>
public sealed class WorldManager
{
    /// <summary>The persistent open-world copy of a map. Dungeons/layers will use higher ids.</summary>
    public const uint OpenWorldInstance = 1;

    private readonly Dictionary<(ushort MapId, uint InstanceId), MapInstance> _instances = new();

    public WorldManager(ContentDatabase content)
    {
        Content = content;
        foreach (var map in content.Maps.Values)
            _instances.Add((map.Id, OpenWorldInstance), new MapInstance(map, OpenWorldInstance));
    }

    public ContentDatabase Content { get; }
    public IEnumerable<MapInstance> Instances => _instances.Values;
    public int PlayerCount => _instances.Values.Sum(i => i.PlayerCount);
    public int EntityCount => _instances.Values.Sum(i => i.EntityCount);

    public MapInstance? GetInstance(ushort mapId, uint instanceId) =>
        _instances.TryGetValue((mapId, instanceId), out var instance) ? instance : null;

    public void Tick(long nowMs, long tick)
    {
        foreach (var instance in _instances.Values) instance.Tick(nowMs, tick);
    }
}
