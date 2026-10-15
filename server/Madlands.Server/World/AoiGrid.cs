using Madlands.Shared;

namespace Madlands.Server.World;

/// <summary>
/// Uniform spatial grid on the XZ plane used for area-of-interest queries ("who is near whom").
/// Cells are created lazily, so sparse worlds cost nothing.
/// </summary>
public sealed class AoiGrid
{
    public const float CellSize = 64f;

    private readonly Dictionary<long, List<Entity>> _cells = new();

    public int CellCount => _cells.Count;

    public static long CellKey(Vec3 position)
    {
        int cx = (int)MathF.Floor(position.X / CellSize);
        int cz = (int)MathF.Floor(position.Z / CellSize);
        return Pack(cx, cz);
    }

    private static long Pack(int cx, int cz) => ((long)cx << 32) | (uint)cz;

    public void Add(Entity entity)
    {
        entity.AoiCell = CellKey(entity.Position);
        GetOrCreate(entity.AoiCell).Add(entity);
    }

    public void Remove(Entity entity)
    {
        if (!_cells.TryGetValue(entity.AoiCell, out var list)) return;
        list.Remove(entity);
        if (list.Count == 0) _cells.Remove(entity.AoiCell);
    }

    /// <summary>Call after an entity's position changed.</summary>
    public void Move(Entity entity)
    {
        long key = CellKey(entity.Position);
        if (key == entity.AoiCell) return;
        Remove(entity);
        entity.AoiCell = key;
        GetOrCreate(key).Add(entity);
    }

    /// <summary>Appends all entities within <paramref name="radius"/> (horizontal distance) to <paramref name="results"/>.</summary>
    public void Query(Vec3 center, float radius, List<Entity> results)
    {
        int minX = (int)MathF.Floor((center.X - radius) / CellSize);
        int maxX = (int)MathF.Floor((center.X + radius) / CellSize);
        int minZ = (int)MathF.Floor((center.Z - radius) / CellSize);
        int maxZ = (int)MathF.Floor((center.Z + radius) / CellSize);
        float radiusSq = radius * radius;

        for (int cx = minX; cx <= maxX; cx++)
        {
            for (int cz = minZ; cz <= maxZ; cz++)
            {
                if (!_cells.TryGetValue(Pack(cx, cz), out var list)) continue;
                foreach (var entity in list)
                {
                    if (Vec3.HorizontalDistanceSquared(entity.Position, center) <= radiusSq)
                        results.Add(entity);
                }
            }
        }
    }

    private List<Entity> GetOrCreate(long key)
    {
        if (!_cells.TryGetValue(key, out var list))
        {
            list = new List<Entity>(4);
            _cells.Add(key, list);
        }
        return list;
    }
}
