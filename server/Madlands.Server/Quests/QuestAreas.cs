using Madlands.Content;
using Madlands.Shared.Game;

namespace Madlands.Server.Quests;

/// <summary>
/// Works out where a quest objective can be done, for the world map: the explore place itself, or circles
/// around the spawn points of the creatures involved (the ones to kill, the ones whose loot drops the item
/// to collect, else the vendors selling it, the one to talk to). Spawns close to each other share a circle.
/// </summary>
public static class QuestAreas
{
    /// <summary>A spawn joins a circle whose center is at most this far away (meters).</summary>
    public const float ClusterRange = 70f;
    /// <summary>Added around the outermost spawn so the circle covers where the creatures roam.</summary>
    public const float Margin = 8f;
    public const float MinRadius = 15f;
    public const float NpcRadius = 10f;

    public static List<QuestArea> For(ContentDatabase content, QuestObjectiveDefinition objective)
    {
        switch (objective.Type)
        {
            case ObjectiveType.Explore:
                return new List<QuestArea> { new QuestArea { Map = (ushort)objective.Map, X = objective.X, Z = objective.Z, Radius = objective.Radius } };
            case ObjectiveType.Kill:
                return Around(content, id => id == objective.Creature, MinRadius);
            case ObjectiveType.Talk:
                return Around(content, id => id == objective.Creature, NpcRadius);
            case ObjectiveType.Collect:
                var droppers = Around(content, id => content.Creatures.TryGetValue(id, out var creature) && Drops(content, creature, objective.Item), MinRadius);
                return droppers.Count > 0 ? droppers
                    : Around(content, id => content.Creatures.TryGetValue(id, out var creature) && creature.Vendor.Contains(objective.Item), NpcRadius);
            default:
                return new List<QuestArea>();
        }
    }

    /// <summary>Whether the creature's loot table can drop the item.</summary>
    public static bool Drops(ContentDatabase content, CreatureDefinition creature, uint item)
    {
        if (creature.Loot == 0 || !content.LootTables.TryGetValue(creature.Loot, out var table)) return false;
        return table.Items.Any(i => i.Item == item && i.Chance > 0f) || table.Groups.Any(g => g.Chance > 0f && g.Items.Contains(item));
    }

    /// <summary>
    /// Circles around the spawns of the matching creatures on every map, the busiest first. Spawns join the
    /// first circle in range; then circles reaching another one's center merge.
    /// </summary>
    private static List<QuestArea> Around(ContentDatabase content, Func<int, bool> matches, float minRadius)
    {
        var clusters = new List<Cluster>();
        foreach (var map in content.Maps.Values.OrderBy(m => m.Id))
        {
            foreach (var spawn in map.Spawns)
            {
                if (!matches(spawn.Creature)) continue;
                var cluster = clusters.FirstOrDefault(c => c.Map == map.Id && c.DistanceTo(spawn.X, spawn.Z) <= ClusterRange);
                if (cluster == null) clusters.Add(cluster = new Cluster(map.Id));
                cluster.Spawns.Add(spawn);
            }
        }
        for (bool merged = true; merged;)
        {
            merged = false;
            for (int i = 0; i < clusters.Count && !merged; i++)
            for (int j = i + 1; j < clusters.Count && !merged; j++)
            {
                if (clusters[i].Map != clusters[j].Map) continue;
                QuestArea a = clusters[i].ToArea(minRadius), b = clusters[j].ToArea(minRadius);
                float dx = a.X - b.X, dz = a.Z - b.Z;
                if (MathF.Sqrt(dx * dx + dz * dz) >= MathF.Max(a.Radius, b.Radius)) continue;
                clusters[i].Spawns.AddRange(clusters[j].Spawns);
                clusters.RemoveAt(j);
                merged = true;
            }
        }
        return clusters
            .OrderByDescending(c => c.Spawns.Count)
            .Take(QuestObjectiveInfo.MaxAreas)
            .Select(c => c.ToArea(minRadius))
            .ToList();
    }

    private sealed class Cluster
    {
        public Cluster(ushort map) => Map = map;

        public ushort Map { get; }
        public List<SpawnDefinition> Spawns { get; } = new();
        private float CenterX => Spawns.Average(s => s.X);
        private float CenterZ => Spawns.Average(s => s.Z);

        public float DistanceTo(float x, float z) => MathF.Sqrt((x - CenterX) * (x - CenterX) + (z - CenterZ) * (z - CenterZ));

        public QuestArea ToArea(float minRadius)
        {
            float x = CenterX, z = CenterZ;
            float reach = Spawns.Max(s => MathF.Sqrt((s.X - x) * (s.X - x) + (s.Z - z) * (s.Z - z)) + s.WanderRadius);
            return new QuestArea { Map = Map, X = x, Z = z, Radius = MathF.Max(minRadius, reach + Margin) };
        }
    }
}
