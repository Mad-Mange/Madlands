using System.Diagnostics;
using Madlands.ClientCore;
using Madlands.Content;
using Madlands.Shared;
using Madlands.Shared.Movement;
using Madlands.Shared.Terrain;

namespace Madlands.Server.Tests;

/// <summary>Real server + real clients over UDP loopback, driven tick by tick from the test.</summary>
public class IntegrationTests
{
    private static ContentDatabase TestContent()
    {
        var def = new MapDefinition { Id = 1, Name = "Test", Spawn = new SpawnPoint { X = 0, Z = 0 } };
        return new ContentDatabase(new[] { new LoadedMap(def, HeightMap.CreateFlat(-200, -200, 4, 101, 101, 10f)) });
    }

    private static void Pump(GameServer server, GameClient[] clients, Func<bool> until, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!until())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Condition not reached in time.");
            server.Tick();
            foreach (var c in clients) c.Update();
            Thread.Sleep(5);
        }
    }

    private static void PumpFor(GameServer server, GameClient[] clients, int ms)
    {
        var sw = Stopwatch.StartNew();
        Pump(server, clients, () => sw.ElapsedMilliseconds >= ms, ms + 1000);
    }

    [Fact]
    public void Players_see_each_other_move_and_leave()
    {
        using var server = new GameServer(new ServerOptions { Port = 0, Bind = "127.0.0.1" }, TestContent());
        server.Start();
        using var alice = new GameClient(manualMode: true);
        using var bob = new GameClient(manualMode: true);
        var clients = new[] { alice, bob };

        MovementState aliceSpawn = default;
        alice.EnteredWorld += s => aliceSpawn = s;
        alice.Connect("127.0.0.1", server.Port, "Alice");
        bob.Connect("127.0.0.1", server.Port, "Bob");

        Pump(server, clients, () => alice.State == ClientState.InWorld && bob.State == ClientState.InWorld);
        Assert.Equal(EntityType.Player, alice.LocalGuid.Type);
        Assert.Equal(10f, aliceSpawn.Position.Y, 3);

        Pump(server, clients, () => bob.World.TryGet(alice.LocalGuid, out _));
        Assert.Equal("Alice", bob.World.Entities.Single().Name);

        // Alice runs 3 m along +X after enough time has passed for that to be legal.
        PumpFor(server, clients, 600);
        var moved = new MovementState(aliceSpawn.Position + new Vec3(3, 0, 0), 0, Vec3.Zero, MovementFlags.None);
        alice.SendMovement(moved);
        Pump(server, clients, () =>
            bob.World.TryGet(alice.LocalGuid, out var e) &&
            Vec3.HorizontalDistance(e.Movement.Newest.Position, moved.Position) < 0.001f);

        alice.Disconnect();
        Pump(server, new[] { bob }, () => !bob.World.TryGet(alice.LocalGuid, out _));
        Assert.Equal(1, server.World.PlayerCount);
    }

    [Fact]
    public void Impossible_move_is_corrected()
    {
        using var server = new GameServer(new ServerOptions { Port = 0, Bind = "127.0.0.1" }, TestContent());
        server.Start();
        using var cheater = new GameClient(manualMode: true);
        var clients = new[] { cheater };

        MovementState spawn = default;
        MovementState? correction = null;
        cheater.EnteredWorld += s => spawn = s;
        cheater.Corrected += s => correction = s;
        cheater.Connect("127.0.0.1", server.Port, "Cheater");
        Pump(server, clients, () => cheater.State == ClientState.InWorld);

        cheater.SendMovement(MovementState.Idle(spawn.Position + new Vec3(100, 0, 0), 0));
        Pump(server, clients, () => correction.HasValue);
        Assert.True(Vec3.HorizontalDistance(correction!.Value.Position, spawn.Position) < 0.01f);
    }

    [Fact]
    public void Invalid_name_is_rejected_with_reason()
    {
        using var server = new GameServer(new ServerOptions { Port = 0, Bind = "127.0.0.1" }, TestContent());
        server.Start();
        using var client = new GameClient(manualMode: true);
        string? reason = null;
        client.Disconnected += r => reason = r;
        client.Connect("127.0.0.1", server.Port, "<bad>");
        Pump(server, new[] { client }, () => reason is not null);
        Assert.Contains("InvalidName", reason);
    }
}

public class ContentLoaderTests
{
    [Fact]
    public void Loads_map_and_reports_all_errors()
    {
        var root = Directory.CreateTempSubdirectory("madlands-content-");
        try
        {
            var good = Directory.CreateDirectory(Path.Combine(root.FullName, "maps", "0001"));
            File.WriteAllText(Path.Combine(good.FullName, "map.json"), """{ "id": 1, "name": "Good", "spawn": { "x": 5, "z": 5 } }""");
            HeightMap.CreateFlat(0, 0, 1, 11, 11, 3f).Save(Path.Combine(good.FullName, "height.bin"));

            var db = ContentLoader.Load(root.FullName);
            Assert.Equal(new Vec3(5, 3, 5), db.Maps[1].SpawnPosition);

            var bad = Directory.CreateDirectory(Path.Combine(root.FullName, "maps", "0002"));
            File.WriteAllText(Path.Combine(bad.FullName, "map.json"), """{ "id": 1, "name": "", "spawn": { "x": 0, "z": 0 } }""");
            var ex = Assert.Throws<ContentException>(() => ContentLoader.Load(root.FullName));
            Assert.Contains(ex.Errors, e => e.Contains("already used"));
            Assert.Contains(ex.Errors, e => e.Contains("name is required"));
            Assert.Contains(ex.Errors, e => e.Contains("height map"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
