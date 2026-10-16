using System.Diagnostics;
using Madlands.Content;
using Madlands.Navigation;
using Madlands.Shared;
using Madlands.Shared.Terrain;
using Xunit.Abstractions;

namespace Madlands.Server.Tests;

public class NavigationTests
{
    private readonly ITestOutputHelper _output;

    public NavigationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>Axis-aligned box as 12 triangles (what the Unity exporter writes for a BoxCollider).</summary>
    internal static CollisionMesh Box(Vec3 min, Vec3 max)
    {
        var v = new List<float>();
        for (int i = 0; i < 8; i++)
        {
            v.Add((i & 1) == 0 ? min.X : max.X);
            v.Add((i & 2) == 0 ? min.Y : max.Y);
            v.Add((i & 4) == 0 ? min.Z : max.Z);
        }
        int[] quads = { 0, 1, 3, 2, 4, 6, 7, 5, 0, 4, 5, 1, 2, 3, 7, 6, 0, 2, 6, 4, 1, 5, 7, 3 };
        var tris = new List<int>();
        for (int q = 0; q < quads.Length; q += 4)
        {
            tris.AddRange(new[] { quads[q], quads[q + 1], quads[q + 2] });
            tris.AddRange(new[] { quads[q], quads[q + 2], quads[q + 3] });
        }
        return new CollisionMesh(v.ToArray(), tris.ToArray());
    }

    [Fact]
    public void Collision_mesh_file_roundtrip()
    {
        var box = Box(new Vec3(-1, 0, -2), new Vec3(1, 3, 2));
        using var stream = new MemoryStream();
        box.Write(stream);
        stream.Position = 0;
        var copy = CollisionMesh.Read(stream);
        Assert.Equal(box.Vertices, copy.Vertices);
        Assert.Equal(box.Indices, copy.Indices);
        Assert.Throws<InvalidDataException>(() => CollisionMesh.Read(new MemoryStream("MLHM"u8.ToArray())));
    }

    [Fact]
    public void Path_goes_around_a_wall()
    {
        var ground = HeightMap.CreateFlat(-30, -30, 1, 61, 61, 5f);
        // A wall 16 m wide across the straight line from (0, -10) to (0, 10).
        var wall = Box(new Vec3(-8, 4, -1), new Vec3(8, 9, 1));
        var nav = NavMesh.Build(ground, wall);
        _output.WriteLine($"{nav.TileCount} tiles, {nav.PolyCount} polys in {nav.BuildTime.TotalMilliseconds:0} ms");
        var query = nav.CreateQuery();

        var corners = new List<Vec3>();
        Assert.Equal(PathResult.Complete, query.FindPath(new Vec3(0, 5, -10), new Vec3(0, 5, 10), corners));
        Assert.True(corners.Count >= 3, "expected a detour around the wall");
        float length = 0;
        for (int i = 1; i < corners.Count; i++) length += Vec3.Distance(corners[i - 1], corners[i]);
        Assert.True(length > 25f, $"path length {length}");
        // No corner inside the wall (plus the agent radius).
        Assert.DoesNotContain(corners, c => MathF.Abs(c.X) < 8.4f && MathF.Abs(c.Z) < 1.4f);

        // The wall top (y = 9) is not walkable: the nearest navmesh point is on the ground beside it.
        if (query.TryGetNearest(new Vec3(0, 9, 0), out var nearWall)) Assert.True(nearWall.Y < 6f, $"walkable wall top at {nearWall}");
        Assert.True(query.TryRandomPointAround(new Vec3(0, 5, -10), 5f, new Random(1), out var random));
        Assert.True(Vec3.HorizontalDistance(random, new Vec3(0, 5, -10)) <= 5.5f);
    }

    [Fact]
    public void Line_of_sight_is_blocked_by_obstacles_and_terrain()
    {
        // A 10 m high ridge along x = 0, flat ground elsewhere.
        var ground = HeightMap.Create(-50, -50, 1, 101, 101, (x, z) => MathF.Abs(x) < 3 ? 10f : 0f);
        var pillar = Box(new Vec3(19, -1, -1), new Vec3(21, 6, 1));
        var world = new CollisionWorld(ground, pillar);

        Assert.True(world.HasLineOfSight(new Vec3(10, 1.5f, -20), new Vec3(10, 1.5f, 20)));
        Assert.False(world.HasLineOfSight(new Vec3(-10, 1.5f, 0), new Vec3(10, 1.5f, 0)), "ridge blocks");
        Assert.True(world.HasLineOfSight(new Vec3(-10, 12, 0), new Vec3(10, 12, 0)), "above the ridge");
        Assert.False(world.HasLineOfSight(new Vec3(15, 1.5f, 0), new Vec3(25, 1.5f, 0)), "pillar blocks");
        Assert.True(world.HasLineOfSight(new Vec3(15, 7, 0), new Vec3(25, 7, 0)), "over the pillar");
        Assert.True(world.Raycast(new Vec3(15, 1.5f, 0), new Vec3(25, 1.5f, 0), out float fraction));
        Assert.Equal(0.4f, fraction, 2);
        // Diagonal through several grid cells, and backwards.
        Assert.False(world.HasLineOfSight(new Vec3(30, 2, 10), new Vec3(10, 2, -10)));
        Assert.False(world.HasLineOfSight(new Vec3(10, 2, -10), new Vec3(30, 2, 10)));
    }

    [Fact]
    public void Repository_map_builds_a_navmesh()
    {
        var root = ContentPaths.FindContentRoot(AppContext.BaseDirectory)!;
        var map = ContentLoader.Load(root).Maps[1];
        var sw = Stopwatch.StartNew();
        var nav = NavMesh.Build(map.HeightMap, map.Collision);
        _output.WriteLine($"Map 1: {nav.TileCount} tiles, {nav.PolyCount} polys, {map.Collision.TriangleCount} obstacle triangles in {sw.ElapsedMilliseconds} ms");
        var query = nav.CreateQuery();
        Assert.True(query.TryGetNearest(map.SpawnPosition, out var nearest));
        Assert.True(Vec3.HorizontalDistance(nearest, map.SpawnPosition) < 1f);
        var corners = new List<Vec3>();
        Assert.Equal(PathResult.Complete, query.FindPath(map.SpawnPosition, map.SpawnPosition + new Vec3(60, 0, 60), corners));
    }
}
