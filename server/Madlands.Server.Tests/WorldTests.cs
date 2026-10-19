using Madlands.Server.Net;
using Madlands.Server.World;
using Madlands.Shared;
using Madlands.Shared.Movement;
using Madlands.Shared.Terrain;

namespace Madlands.Server.Tests;

public class HeightMapTests
{
    [Fact]
    public void Bilinear_sampling_and_file_roundtrip()
    {
        // Height = x + 2z, a plane, so bilinear interpolation must be exact everywhere.
        var map = HeightMap.Create(-10, -20, 2, 11, 21, (x, z) => x + 2 * z);
        Assert.Equal(10f, map.MaxX);
        Assert.Equal(20f, map.MaxZ);
        Assert.Equal(1.5f + 2 * 3.25f, map.GetHeight(1.5f, 3.25f), 4);
        Assert.Equal(map.GetHeight(10, 20), map.GetHeight(999, 999)); // clamped outside
        Assert.True(map.Contains(0, 0));
        Assert.False(map.Contains(10.1f, 0));

        using var stream = new MemoryStream();
        map.Write(stream);
        stream.Position = 0;
        var copy = HeightMap.Read(stream);
        Assert.Equal((map.OriginX, map.OriginZ, map.CellSize, map.Width, map.Depth), (copy.OriginX, copy.OriginZ, copy.CellSize, copy.Width, copy.Depth));
        Assert.Equal(map.GetHeight(-3.3f, 7.7f), copy.GetHeight(-3.3f, 7.7f));
    }

    [Fact]
    public void Rejects_foreign_files()
    {
        using var stream = new MemoryStream("NOPE"u8.ToArray());
        Assert.Throws<InvalidDataException>(() => HeightMap.Read(stream));
    }
}

public class AoiGridTests
{
    private static Entity At(float x, float z, ulong id = 1)
    {
        var e = new Entity(EntityGuid.Create(EntityType.Creature, id), "e" + id, 0);
        e.Movement = MovementState.Idle(new Vec3(x, 0, z), 0);
        return e;
    }

    [Fact]
    public void Query_finds_entities_within_radius_across_cells()
    {
        var grid = new AoiGrid();
        var near = At(60, 60, 1);      // different cell from origin, within 100 m
        var far = At(300, 0, 2);
        var negative = At(-70, -10, 3);
        grid.Add(near);
        grid.Add(far);
        grid.Add(negative);

        var results = new List<Entity>();
        grid.Query(Vec3.Zero, 100, results);
        Assert.Contains(near, results);
        Assert.Contains(negative, results);
        Assert.DoesNotContain(far, results);
    }

    [Fact]
    public void Move_updates_cell_membership()
    {
        var grid = new AoiGrid();
        var e = At(0, 0);
        grid.Add(e);
        e.Movement.Position = new Vec3(500, 0, 500);
        grid.Move(e);

        var results = new List<Entity>();
        grid.Query(Vec3.Zero, 50, results);
        Assert.Empty(results);
        grid.Query(new Vec3(500, 0, 500), 10, results);
        Assert.Single(results);

        grid.Remove(e);
        Assert.Equal(0, grid.CellCount);
    }
}

public class MovementValidatorTests
{
    private static readonly HeightMap Flat = HeightMap.CreateFlat(-500, -500, 4, 251, 251, 10f);

    private static MovementState Running(float x, float z) =>
        new(new Vec3(x, 10f, z), 0f, new Vec3(0, 0, GameConstants.RunSpeed), MovementFlags.Forward);

    private static MovementValidator Spawned(long serverNow = 0)
    {
        var v = new MovementValidator(Flat);
        v.Reset(MovementState.Idle(new Vec3(0, 10, 0), 0), serverNow);
        return v;
    }

    [Fact]
    public void Accepts_normal_running()
    {
        var v = Spawned();
        Assert.Equal(MoveVerdict.Accepted, v.Validate(Running(0, 0), clientMs: 1000, serverNowMs: 1000));
        // 500 ms at 7 m/s = 3.5 m
        Assert.Equal(MoveVerdict.Accepted, v.Validate(Running(0, 3.5f), 1500, 1500));
        Assert.Equal(MoveVerdict.Accepted, v.Validate(Running(0, 7f), 2000, 2010));
    }

    [Fact]
    public void Rejects_teleport()
    {
        var v = Spawned();
        Assert.Equal(MoveVerdict.Accepted, v.Validate(Running(0, 0), 1000, 1000));
        Assert.Equal(MoveVerdict.Rejected, v.Validate(Running(0, 20f), 1500, 1500));
        Assert.Contains("moved", v.LastRejectReason);
    }

    [Fact]
    public void Rejects_client_clock_running_faster_than_server()
    {
        var v = Spawned();
        Assert.Equal(MoveVerdict.Accepted, v.Validate(Running(0, 0), 1000, 1000));
        // Speed hack: client claims 3 s passed while only 1 s passed on the server.
        Assert.Equal(MoveVerdict.Rejected, v.Validate(Running(0, 6f), 4000, 2000));
        Assert.Contains("clock", v.LastRejectReason);
    }

    [Fact]
    public void Caps_time_step_so_idle_time_cannot_be_banked()
    {
        var v = Spawned();
        Assert.Equal(MoveVerdict.Accepted, v.Validate(Running(0, 0), 1000, 1000));
        // 10 s later, a 60 m jump would fit 10 s of running but not the 1 s cap.
        Assert.Equal(MoveVerdict.Rejected, v.Validate(Running(0, 60f), 11000, 11000));
    }

    [Fact]
    public void Ignores_stale_updates()
    {
        var v = Spawned();
        Assert.Equal(MoveVerdict.Accepted, v.Validate(Running(0, 0), 1000, 1000));
        Assert.Equal(MoveVerdict.Stale, v.Validate(Running(0, 0), 900, 1100));
    }

    [Fact]
    public void Rejects_underground_flying_out_of_bounds_and_nan()
    {
        var v = Spawned();
        Assert.Equal(MoveVerdict.Rejected, v.Validate(MovementState.Idle(new Vec3(0, 5f, 0), 0), 1000, 1000));
        Assert.Equal(MoveVerdict.Rejected, v.Validate(MovementState.Idle(new Vec3(0, 25f, 0), 0), 1000, 1000));
        Assert.Equal(MoveVerdict.Rejected, v.Validate(MovementState.Idle(new Vec3(float.NaN, 10f, 0), 0), 1000, 1000));

        var edge = new MovementValidator(Flat);
        edge.Reset(MovementState.Idle(new Vec3(499, 10, 0), 0), 0);
        Assert.Equal(MoveVerdict.Rejected, edge.Validate(MovementState.Idle(new Vec3(501, 10f, 0), 0), 1000, 1000));
    }

    [Fact]
    public void Rejects_reported_velocity_above_run_speed()
    {
        var v = Spawned();
        var state = Running(0, 0);
        state.Velocity = new Vec3(0, 0, 20);
        Assert.Equal(MoveVerdict.Rejected, v.Validate(state, 1000, 1000));
    }

    private static MovementState Sprinting(float z) =>
        new(new Vec3(0, 10f, z), 0f, new Vec3(0, 0, GameConstants.RunSpeed * Stamina.SpeedMultiplier),
            MovementFlags.Forward | MovementFlags.Sprinting);

    [Fact]
    public void Sprinting_is_faster_while_the_stamina_lasts()
    {
        float sprintSpeed = GameConstants.RunSpeed * Stamina.SpeedMultiplier;
        var v = Spawned();
        Assert.Equal(MoveVerdict.Accepted, v.Validate(Sprinting(0), 1000, 1000));
        // Without the sprint flag the same speed is too fast.
        var plain = Spawned();
        Assert.Equal(MoveVerdict.Accepted, plain.Validate(Running(0, 0), 1000, 1000));
        Assert.Equal(MoveVerdict.Rejected, plain.Validate(Running(0, sprintSpeed * 0.5f), 1500, 1500));

        // Full stamina lasts Max / DrainPerSecond seconds (about 5.5 s); then the grace, then no more.
        float z = 0f;
        long t = 1000;
        while (v.Stamina.Value > 0f)
        {
            t += 500;
            z += sprintSpeed * 0.5f;
            Assert.Equal(MoveVerdict.Accepted, v.Validate(Sprinting(z), t, t));
        }
        Assert.InRange(t - 1000, 5000, 6000);
        t += MovementValidator.SprintGraceMs + 500;
        z += sprintSpeed * (MovementValidator.SprintGraceMs + 500) / 1000f;
        Assert.Equal(MoveVerdict.Rejected, v.Validate(Sprinting(z), t, t));
    }

    [Fact]
    public void Stamina_drains_while_sprinting_and_refills_after_a_rest()
    {
        var stamina = new Stamina();
        Assert.True(stamina.CanSprint);
        stamina.Advance(true, 2f, 2000);
        Assert.Equal(Stamina.Max - 2 * Stamina.DrainPerSecond, stamina.Value, 3);
        stamina.Advance(true, 10f, 12000);
        Assert.Equal(0f, stamina.Value);
        Assert.False(stamina.CanSprint);

        // No refill during the rest delay, then a steady refill; a new sprint needs a little first.
        stamina.Advance(false, 1f, 13000);
        Assert.Equal(0f, stamina.Value);
        stamina.Advance(false, 0.5f, 13600);
        Assert.Equal(0.5f * Stamina.RegenPerSecond, stamina.Value, 3);
        Assert.Equal(Stamina.ToStart <= stamina.Value, stamina.CanSprint);
        stamina.Advance(false, 60f, 73600);
        Assert.Equal(Stamina.Max, stamina.Value);
        Assert.True(stamina.CanSprint);
    }
}

public class ServerMiscTests
{
    [Theory]
    [InlineData("Åsa", "Åsa")]       // non-ASCII letters are allowed
    [InlineData("  aLiCe ", "Alice")] // normalized capitalization
    [InlineData("Botaaa", "Botaaa")]
    [InlineData("A", null)]
    [InlineData("Bot007", null)]
    [InlineData("Gandalf the Grey", null)]
    [InlineData("<script>", null)]
    [InlineData("Thisnameiswaytoolong", null)]
    public void Character_names_are_normalized(string input, string? expected) =>
        Assert.Equal(expected, NameRules.NormalizeCharacterName(input));

    [Theory]
    [InlineData(" Alice.B-1_ ", "alice.b-1_")]
    [InlineData("ab", null)]
    [InlineData("has space", null)]
    [InlineData("åsa", null)]
    public void Usernames_are_normalized(string input, string? expected) =>
        Assert.Equal(expected, NameRules.NormalizeUsername(input));

    [Fact]
    public void Rate_limiter_allows_burst_then_refills()
    {
        var limiter = new RateLimiter(ratePerSecond: 10, burst: 3);
        Assert.True(limiter.TryConsume(0));
        Assert.True(limiter.TryConsume(0));
        Assert.True(limiter.TryConsume(0));
        Assert.False(limiter.TryConsume(0));
        Assert.True(limiter.TryConsume(100)); // 100 ms at 10/s = one token
        Assert.False(limiter.TryConsume(100));
    }
}

public class MoveBroadcastTests
{
    [Fact]
    public void Far_movement_is_sent_less_often()
    {
        Assert.Equal(1u, MapInstance.MoveSendInterval(30f * 30f));
        Assert.Equal(2u, MapInstance.MoveSendInterval(80f * 80f));
        Assert.Equal(4u, MapInstance.MoveSendInterval(140f * 140f));

        // Over any four broadcasts a far entity goes out once, a mid-range one twice.
        var guid = EntityGuid.Create(EntityType.Player, 7);
        Assert.Equal(1, Enumerable.Range(10, 4).Count(b => MapInstance.IsMoveDue(guid, 4, (uint)b)));
        Assert.Equal(2, Enumerable.Range(10, 4).Count(b => MapInstance.IsMoveDue(guid, 2, (uint)b)));
        // Different entities are spread over the broadcasts.
        var due = Enumerable.Range(1, 8).Select(c => Enumerable.Range(0, 4).First(b => MapInstance.IsMoveDue(EntityGuid.Create(EntityType.Creature, (ulong)c), 4, (uint)b)));
        Assert.Equal(4, due.Distinct().Count());
    }
}
