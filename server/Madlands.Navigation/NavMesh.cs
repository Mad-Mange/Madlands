using System.Diagnostics;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using Madlands.Shared;
using Madlands.Shared.Terrain;

namespace Madlands.Navigation;

/// <summary>Recast parameters. The agent matches the player capsule (CharacterController in the client).</summary>
public sealed class NavSettings
{
    public float CellSize { get; init; } = 0.3f;
    public float CellHeight { get; init; } = 0.2f;
    public float AgentHeight { get; init; } = 2.0f;
    public float AgentRadius { get; init; } = 0.5f;
    public float AgentMaxClimb { get; init; } = 0.5f;
    public float AgentMaxSlope { get; init; } = 50f;
    /// <summary>Tile edge in cells; tiles build in parallel.</summary>
    public int TileSize { get; init; } = 64;
    /// <summary>Spacing of the terrain triangles fed to Recast (meters). Coarser builds faster.</summary>
    public float TerrainSpacing { get; init; } = 2f;
    public int VertsPerPoly { get; init; } = 6;

    public static readonly NavSettings Default = new();
}

/// <summary>
/// A map's walkable surface, built with Recast from the terrain (height map) and the static
/// obstacles. Obstacle triangles are rasterized as non-walkable, so nothing paths across rocks or
/// trees. Immutable after building; every map instance makes its own <see cref="NavQuery"/>.
/// </summary>
public sealed class NavMesh
{
    public const int WalkFlag = 1;

    private NavMesh(DtNavMesh mesh, int tileCount, int polyCount, TimeSpan buildTime)
    {
        Mesh = mesh;
        TileCount = tileCount;
        PolyCount = polyCount;
        BuildTime = buildTime;
    }

    internal DtNavMesh Mesh { get; }
    public int TileCount { get; }
    public int PolyCount { get; }
    public TimeSpan BuildTime { get; }

    /// <summary>Not thread-safe: one per map instance (all its queries run on the instance's thread).</summary>
    public NavQuery CreateQuery() => new(this);

    public static NavMesh Build(HeightMap heightMap, CollisionMesh obstacles, NavSettings? settings = null)
    {
        settings ??= NavSettings.Default;
        var sw = Stopwatch.StartNew();
        var terrain = CreateTerrainMesh(heightMap, settings.TerrainSpacing);
        var obstacleMesh = obstacles.TriangleCount > 0 ? new RcTriMesh(obstacles.Vertices, obstacles.Indices) : null;

        // Bounds: the terrain, plus headroom for obstacles standing on it.
        var bmin = new RcVec3f(heightMap.OriginX, float.MaxValue, heightMap.OriginZ);
        var bmax = new RcVec3f(heightMap.MaxX, float.MinValue, heightMap.MaxZ);
        var verts = terrain.GetVerts();
        for (int i = 1; i < verts.Length; i += 3)
        {
            bmin.Y = MathF.Min(bmin.Y, verts[i]);
            bmax.Y = MathF.Max(bmax.Y, verts[i]);
        }
        for (int i = 1; i < obstacles.Vertices.Length; i += 3)
        {
            bmin.Y = MathF.Min(bmin.Y, obstacles.Vertices[i]);
            bmax.Y = MathF.Max(bmax.Y, obstacles.Vertices[i]);
        }
        bmin.Y -= 1f;
        bmax.Y += settings.AgentHeight + 1f;

        var walkable = new RcAreaModification(RcRecast.RC_WALKABLE_AREA);
        var cfg = new RcConfig(true, settings.TileSize, settings.TileSize,
            RcConfig.CalcBorder(settings.AgentRadius, settings.CellSize),
            RcPartition.WATERSHED,
            settings.CellSize, settings.CellHeight,
            settings.AgentMaxSlope, settings.AgentHeight, settings.AgentRadius, settings.AgentMaxClimb,
            8 * 8 * settings.CellSize * settings.CellSize, 20 * 20 * settings.CellSize * settings.CellSize,
            12f, 1.3f, settings.VertsPerPoly,
            6f, 1f,
            true, true, true,
            walkable, true);

        RcRecast.CalcTileCount(bmin, bmax, cfg.Cs, cfg.TileSizeX, cfg.TileSizeZ, out int tilesX, out int tilesZ);
        int tileBits = Math.Min(DtUtils.Ilog2(DtUtils.NextPow2(tilesX * tilesZ)), 14);
        var navParams = new DtNavMeshParams
        {
            orig = bmin,
            tileWidth = settings.TileSize * settings.CellSize,
            tileHeight = settings.TileSize * settings.CellSize,
            maxTiles = 1 << tileBits,
            maxPolys = 1 << (22 - tileBits),
        };
        var mesh = new DtNavMesh();
        mesh.Init(navParams, settings.VertsPerPoly);

        var tiles = new DtMeshData?[tilesX * tilesZ];
        Parallel.For(0, tiles.Length, index =>
        {
            tiles[index] = BuildTile(terrain, obstacleMesh, cfg, bmin, bmax, index % tilesX, index / tilesX, settings);
        });

        int tileCount = 0, polyCount = 0;
        foreach (var tile in tiles)
        {
            if (tile is null) continue;
            mesh.AddTile(tile, 0, 0, out _);
            tileCount++;
            polyCount += tile.header.polyCount;
        }
        return new NavMesh(mesh, tileCount, polyCount, sw.Elapsed);
    }

    private static DtMeshData? BuildTile(RcTriMesh terrain, RcTriMesh? obstacles, RcConfig cfg, RcVec3f bmin, RcVec3f bmax, int tx, int tz, NavSettings settings)
    {
        var ctx = new RcContext();
        var bcfg = new RcBuilderConfig(cfg, bmin, bmax, tx, tz);
        var solid = new RcHeightfield(bcfg.width, bcfg.height, bcfg.bmin, bcfg.bmax, cfg.Cs, cfg.Ch, cfg.BorderSize);
        var rectMin = new RcVec2f(bcfg.bmin.X, bcfg.bmin.Z);
        var rectMax = new RcVec2f(bcfg.bmax.X, bcfg.bmax.Z);

        foreach (var node in terrain.GetChunksOverlappingRect(rectMin, rectMax))
        {
            int n = node.tris.Length / 3;
            var areas = RcRecast.MarkWalkableTriangles(ctx, cfg.WalkableSlopeAngle, terrain.GetVerts(), node.tris, n, cfg.WalkableAreaMod);
            RcRasterizations.RasterizeTriangles(ctx, terrain.GetVerts(), node.tris, areas, n, solid, cfg.WalkableClimb);
        }
        if (obstacles is not null)
        {
            foreach (var node in obstacles.GetChunksOverlappingRect(rectMin, rectMax))
            {
                int n = node.tris.Length / 3;
                // RC_NULL_AREA: obstacles block and are never walked on.
                RcRasterizations.RasterizeTriangles(ctx, obstacles.GetVerts(), node.tris, new int[n], n, solid, cfg.WalkableClimb);
            }
        }

        var result = new RcBuilder().Build(ctx, tx, tz, null, cfg, solid, false);
        var pmesh = result.Mesh;
        if (pmesh is null || pmesh.npolys == 0) return null;
        for (int i = 0; i < pmesh.npolys; i++) pmesh.flags[i] = WalkFlag;

        var dmesh = result.MeshDetail;
        var option = new DtNavMeshCreateParams
        {
            verts = pmesh.verts,
            vertCount = pmesh.nverts,
            polys = pmesh.polys,
            polyAreas = pmesh.areas,
            polyFlags = pmesh.flags,
            polyCount = pmesh.npolys,
            nvp = pmesh.nvp,
            detailMeshes = dmesh?.meshes,
            detailVerts = dmesh?.verts,
            detailVertsCount = dmesh?.nverts ?? 0,
            detailTris = dmesh?.tris,
            detailTriCount = dmesh?.ntris ?? 0,
            walkableHeight = settings.AgentHeight,
            walkableRadius = settings.AgentRadius,
            walkableClimb = settings.AgentMaxClimb,
            bmin = pmesh.bmin,
            bmax = pmesh.bmax,
            cs = cfg.Cs,
            ch = cfg.Ch,
            buildBvTree = true,
            tileX = tx,
            tileZ = tz,
            offMeshConCount = 0,
            offMeshConVerts = Array.Empty<float>(),
            offMeshConRad = Array.Empty<float>(),
            offMeshConDir = Array.Empty<int>(),
            offMeshConAreas = Array.Empty<int>(),
            offMeshConFlags = Array.Empty<int>(),
            offMeshConUserID = Array.Empty<int>(),
        };
        return DtNavMeshBuilder.CreateNavMeshData(option);
    }

    /// <summary>Two triangles per grid cell, sampled from the height map every <paramref name="spacing"/> meters.</summary>
    private static RcTriMesh CreateTerrainMesh(HeightMap heightMap, float spacing)
    {
        int step = Math.Max(1, (int)MathF.Round(spacing / heightMap.CellSize));
        int w = (heightMap.Width - 1) / step + 1;
        int d = (heightMap.Depth - 1) / step + 1;
        var verts = new float[w * d * 3];
        for (int j = 0; j < d; j++)
        {
            for (int i = 0; i < w; i++)
            {
                int k = (j * w + i) * 3;
                verts[k] = heightMap.OriginX + i * step * heightMap.CellSize;
                verts[k + 1] = heightMap.GetSample(i * step, j * step);
                verts[k + 2] = heightMap.OriginZ + j * step * heightMap.CellSize;
            }
        }
        var tris = new int[(w - 1) * (d - 1) * 6];
        int t = 0;
        for (int j = 0; j < d - 1; j++)
        {
            for (int i = 0; i < w - 1; i++)
            {
                int a = j * w + i, b = a + 1, c = a + w, e = c + 1;
                // Counter-clockwise seen from above (Recast's walkable test uses the normal's Y).
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = e;
            }
        }
        return new RcTriMesh(verts, tris);
    }

    internal static RcVec3f ToRc(Vec3 v) => new(v.X, v.Y, v.Z);
    internal static Vec3 FromRc(RcVec3f v) => new(v.X, v.Y, v.Z);
}

public enum PathResult
{
    /// <summary>No path: start or end is off the navmesh.</summary>
    None,
    /// <summary>The end is unreachable; the path leads to the closest reachable point.</summary>
    Partial,
    Complete,
}

/// <summary>Path and point queries on a <see cref="NavMesh"/>. Not thread-safe.</summary>
public sealed class NavQuery
{
    private const int MaxPathPolys = 256;
    private const int MaxCorners = 64;

    private static readonly RcVec3f SearchExtents = new(2f, 4f, 2f);

    private readonly DtNavMeshQuery _query;
    private readonly DtQueryDefaultFilter _filter = new(NavMesh.WalkFlag, 0, new float[DtDetour.DT_MAX_AREAS].Select(_ => 1f).ToArray());
    private readonly long[] _polys = new long[MaxPathPolys];
    private readonly DtStraightPath[] _corners = new DtStraightPath[MaxCorners];

    internal NavQuery(NavMesh mesh)
    {
        _query = new DtNavMeshQuery(mesh.Mesh);
    }

    /// <summary>Closest point on the navmesh within a few meters of <paramref name="position"/>.</summary>
    public bool TryGetNearest(Vec3 position, out Vec3 nearest)
    {
        var status = _query.FindNearestPoly(NavMesh.ToRc(position), SearchExtents, _filter, out long poly, out var point, out _);
        nearest = NavMesh.FromRc(point);
        return status.Succeeded() && poly != 0;
    }

    /// <summary>Corner points from start to end (start included), following the navmesh around obstacles.</summary>
    public PathResult FindPath(Vec3 start, Vec3 end, List<Vec3> corners)
    {
        corners.Clear();
        var s = NavMesh.ToRc(start);
        var e = NavMesh.ToRc(end);
        if (_query.FindNearestPoly(s, SearchExtents, _filter, out long startRef, out var startPt, out _).Failed() || startRef == 0) return PathResult.None;
        if (_query.FindNearestPoly(e, SearchExtents, _filter, out long endRef, out var endPt, out _).Failed() || endRef == 0) return PathResult.None;

        var status = _query.FindPath(startRef, endRef, startPt, endPt, _filter, _polys, out int polyCount, MaxPathPolys);
        if (status.Failed() || polyCount == 0) return PathResult.None;
        bool partial = status.IsPartial() || _polys[polyCount - 1] != endRef;
        if (partial)
        {
            // Aim for the closest point on the last reachable polygon.
            _query.ClosestPointOnPoly(_polys[polyCount - 1], endPt, out endPt, out _);
        }

        status = _query.FindStraightPath(startPt, endPt, _polys, polyCount, _corners, out int cornerCount, MaxCorners, 0);
        if (status.Failed() || cornerCount == 0) return PathResult.None;
        for (int i = 0; i < cornerCount; i++) corners.Add(NavMesh.FromRc(_corners[i].pos));
        return partial ? PathResult.Partial : PathResult.Complete;
    }

    /// <summary>A random reachable point within roughly <paramref name="radius"/> of <paramref name="center"/>.</summary>
    public bool TryRandomPointAround(Vec3 center, float radius, Random random, out Vec3 point)
    {
        point = center;
        var c = NavMesh.ToRc(center);
        if (_query.FindNearestPoly(c, SearchExtents, _filter, out long startRef, out var startPt, out _).Failed() || startRef == 0) return false;
        var status = _query.FindRandomPointWithinCircle(startRef, startPt, radius, _filter, new RandomAdapter(random), out _, out var result);
        if (status.Failed()) return false;
        point = NavMesh.FromRc(result);
        return true;
    }

    private sealed class RandomAdapter : IRcRand
    {
        private readonly Random _random;
        public RandomAdapter(Random random) => _random = random;
        public float Next() => _random.NextSingle();
        public double NextDouble() => _random.NextDouble();
        public int NextInt32() => _random.Next();
    }
}
