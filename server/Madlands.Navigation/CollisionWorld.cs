using Madlands.Shared;
using Madlands.Shared.Terrain;

namespace Madlands.Navigation;

/// <summary>
/// Ray casts against a map's solid geometry: the terrain (height map) and the static obstacles
/// exported from the Unity scene (<see cref="CollisionMesh"/>). Used for line of sight. Immutable
/// after construction and safe to share between map instances and threads.
/// </summary>
public sealed class CollisionWorld
{
    /// <summary>Size of the XZ grid cells that bucket obstacle triangles.</summary>
    public const float GridCellSize = 8f;
    /// <summary>A ray may dip this far into the terrain before it counts as blocked (smooths over sampling error).</summary>
    public const float TerrainTolerance = 0.15f;

    private readonly HeightMap _heightMap;
    private readonly CollisionMesh _obstacles;
    private readonly float _minX, _minZ;
    private readonly int _gridWidth, _gridDepth;
    private readonly int[][] _cells;

    public CollisionWorld(HeightMap heightMap, CollisionMesh obstacles)
    {
        _heightMap = heightMap;
        _obstacles = obstacles;
        _minX = heightMap.OriginX;
        _minZ = heightMap.OriginZ;
        _gridWidth = Math.Max(1, (int)MathF.Ceiling((heightMap.MaxX - _minX) / GridCellSize));
        _gridDepth = Math.Max(1, (int)MathF.Ceiling((heightMap.MaxZ - _minZ) / GridCellSize));

        var buckets = new List<int>?[_gridWidth * _gridDepth];
        for (int t = 0; t < obstacles.TriangleCount; t++)
        {
            var a = obstacles.GetVertex(obstacles.Indices[t * 3]);
            var b = obstacles.GetVertex(obstacles.Indices[t * 3 + 1]);
            var c = obstacles.GetVertex(obstacles.Indices[t * 3 + 2]);
            int x0 = CellX(MathF.Min(a.X, MathF.Min(b.X, c.X))), x1 = CellX(MathF.Max(a.X, MathF.Max(b.X, c.X)));
            int z0 = CellZ(MathF.Min(a.Z, MathF.Min(b.Z, c.Z))), z1 = CellZ(MathF.Max(a.Z, MathF.Max(b.Z, c.Z)));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    (buckets[z * _gridWidth + x] ??= new List<int>()).Add(t);
        }
        _cells = new int[buckets.Length][];
        for (int i = 0; i < buckets.Length; i++) _cells[i] = buckets[i]?.ToArray() ?? Array.Empty<int>();
    }

    public HeightMap HeightMap => _heightMap;
    public int ObstacleTriangleCount => _obstacles.TriangleCount;

    /// <summary>True if nothing solid lies between the two points.</summary>
    public bool HasLineOfSight(Vec3 from, Vec3 to) => !Raycast(from, to, out _);

    /// <summary>First hit along the segment; <paramref name="fraction"/> is 0..1 along it.</summary>
    public bool Raycast(Vec3 from, Vec3 to, out float fraction)
    {
        bool hit = RaycastObstacles(from, to, out fraction);
        if (RaycastTerrain(from, to, hit ? fraction : 1f, out float terrainFraction))
        {
            fraction = terrainFraction;
            hit = true;
        }
        return hit;
    }

    private bool RaycastTerrain(Vec3 from, Vec3 to, float maxFraction, out float fraction)
    {
        fraction = 1f;
        float length = Vec3.HorizontalDistance(from, to);
        float step = _heightMap.CellSize * 0.5f;
        int samples = Math.Max(1, (int)MathF.Ceiling(length / step));
        for (int i = 1; i < samples; i++)
        {
            float t = (float)i / samples;
            if (t > maxFraction) return false;
            var p = Vec3.Lerp(from, to, t);
            if (p.Y < _heightMap.GetHeight(p.X, p.Z) - TerrainTolerance)
            {
                fraction = t;
                return true;
            }
        }
        return false;
    }

    private bool RaycastObstacles(Vec3 from, Vec3 to, out float fraction)
    {
        fraction = 1f;
        if (_obstacles.TriangleCount == 0) return false;

        // Walk the grid cells the segment crosses on the XZ plane (2D DDA). A triangle spanning
        // several cells may be tested more than once; that is cheaper than tracking visits.
        float fx = (from.X - _minX) / GridCellSize, fz = (from.Z - _minZ) / GridCellSize;
        float tx = (to.X - _minX) / GridCellSize, tz = (to.Z - _minZ) / GridCellSize;
        int cx = (int)MathF.Floor(fx), cz = (int)MathF.Floor(fz);
        float dx = tx - fx, dz = tz - fz;
        int stepX = Math.Sign(dx), stepZ = Math.Sign(dz);
        float tDeltaX = stepX != 0 ? MathF.Abs(1f / dx) : float.PositiveInfinity;
        float tDeltaZ = stepZ != 0 ? MathF.Abs(1f / dz) : float.PositiveInfinity;
        float tMaxX = stepX > 0 ? (cx + 1 - fx) * tDeltaX : stepX < 0 ? (fx - cx) * tDeltaX : float.PositiveInfinity;
        float tMaxZ = stepZ > 0 ? (cz + 1 - fz) * tDeltaZ : stepZ < 0 ? (fz - cz) * tDeltaZ : float.PositiveInfinity;

        bool hit = false;
        for (int guard = 0; guard < 100_000; guard++)
        {
            if (cx >= 0 && cz >= 0 && cx < _gridWidth && cz < _gridDepth)
            {
                foreach (int t in _cells[cz * _gridWidth + cx])
                {
                    if (IntersectTriangle(from, to, t, out float f) && f < fraction)
                    {
                        fraction = f;
                        hit = true;
                    }
                }
            }
            // A hit inside this cell is closer than anything in later cells.
            float cellExit = MathF.Min(tMaxX, tMaxZ);
            if (hit && fraction <= cellExit) return true;
            if (cellExit > 1f) break; // the segment ends in this cell
            if (tMaxX < tMaxZ)
            {
                cx += stepX;
                tMaxX += tDeltaX;
            }
            else
            {
                cz += stepZ;
                tMaxZ += tDeltaZ;
            }
        }
        return hit;
    }

    /// <summary>Möller–Trumbore segment/triangle test, two-sided.</summary>
    private bool IntersectTriangle(Vec3 from, Vec3 to, int triangle, out float fraction)
    {
        fraction = 0f;
        var a = _obstacles.GetVertex(_obstacles.Indices[triangle * 3]);
        var b = _obstacles.GetVertex(_obstacles.Indices[triangle * 3 + 1]);
        var c = _obstacles.GetVertex(_obstacles.Indices[triangle * 3 + 2]);
        var dir = to - from;
        var e1 = b - a;
        var e2 = c - a;
        var p = Cross(dir, e2);
        float det = Dot(e1, p);
        if (MathF.Abs(det) < 1e-9f) return false;
        float inv = 1f / det;
        var s = from - a;
        float u = Dot(s, p) * inv;
        if (u < 0f || u > 1f) return false;
        var q = Cross(s, e1);
        float v = Dot(dir, q) * inv;
        if (v < 0f || u + v > 1f) return false;
        float t = Dot(e2, q) * inv;
        if (t < 0f || t > 1f) return false;
        fraction = t;
        return true;
    }

    private int CellX(float x) => Math.Clamp((int)MathF.Floor((x - _minX) / GridCellSize), 0, _gridWidth - 1);
    private int CellZ(float z) => Math.Clamp((int)MathF.Floor((z - _minZ) / GridCellSize), 0, _gridDepth - 1);

    private static Vec3 Cross(Vec3 a, Vec3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    private static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
