using Madlands.ClientCore;
using Madlands.Content;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Net;

namespace Madlands.Server.Tests;

/// <summary>
/// The developer GM menu's command path end to end (docs/GMMenu): a real realm with the repository's content,
/// real clients over UDP, commands sent through <see cref="GmCommandClient"/> exactly as the menu and the
/// Playtest Controller send them. The map spawn is (0, 0) facing +Z.
/// </summary>
public class GmCommandIntegrationTests
{
    private const int MireCroaker = 1;
    private const int DuskWolf = 2;
    private const uint PickHammer = 1260; // one per bag slot

    private sealed class Gm : IDisposable
    {
        public Gm(TestRealm realm, GameClient client)
        {
            Realm = realm;
            Client = client;
            client.ChatReceived += c => Chat.Add(c.Text);
            client.GmGeometryReceived += g => Geometry.Add((g.Kind, g.SegmentCount));
        }

        public TestRealm Realm { get; }
        public GameClient Client { get; }
        public List<string> Chat { get; } = new();
        public List<(GmGeometryKind Kind, int Segments)> Geometry { get; } = new();

        public void Pump(Func<bool> until, int timeoutMs = 8000) => Realm.Pump(new[] { Client }, until, timeoutMs);

        /// <summary>Runs a command the way the menu does and waits for its result.</summary>
        public GmCommandResult Run(string commandId, params (string Key, string Value)[] args)
        {
            GmCommandResult? result = null;
            Client.Gm.Execute(commandId, args.Select(a => new KeyValuePair<string, string>(a.Key, a.Value)).ToList(), r => result = r);
            Pump(() => result is not null);
            return result!;
        }

        public void Command(string text)
        {
            int before = Chat.Count;
            Client.SendChat(text);
            Pump(() => Chat.Count > before);
        }

        public string Data(GmCommandResult result, string key) => result.Data.Single(d => d.Key == key).Value;

        public void Dispose() => Client.Dispose();
    }

    private static SpawnDefinition At(int creature, float x, float z) => new() { Creature = creature, X = x, Z = z, YawDegrees = 180 };

    private static async Task<(TestRealm Realm, Gm Gm)> StartAsync(bool gm, params SpawnDefinition[] spawns)
    {
        var realm = await TestRealm.StartAsync(content: TestRealm.CombatContent(spawns), gmAll: gm);
        var (account, character) = await realm.NewPlayerAsync("tester", "Tester");
        var session = new Gm(realm, realm.Enter(account, character));
        session.Pump(() => session.Client.State == ClientState.InWorld && session.Client.KnownSpells.Count > 0);
        if (gm) session.Pump(() => session.Client.GmStatus.GmLevel > 0 && session.Client.GmCatalog.Destinations.Count > 0);
        return (realm, session);
    }

    [Fact]
    public async Task Players_without_gm_rights_are_refused_even_when_they_send_directly()
    {
        var (realm, player) = await StartAsync(gm: false);
        await using var _ = realm;
        using var __ = player;
        Assert.False(player.Client.Gm.IsAvailable);
        Assert.Equal(0, player.Client.GmStatus.GmLevel);

        // The client's own check stops it before the network...
        var local = player.Run(GmCommands.GodMode, ("enabled", "true"));
        Assert.Equal(GmResultCode.Unauthorized, local.Code);

        // ...and the server refuses a hand-made request all the same.
        GmCommandResult? answer = null;
        player.Client.GmResultReceived += r => answer ??= new GmCommandResult { RequestId = r.RequestId, Code = r.Code, Message = r.Message };
        player.Client.SendGmCommand(42, GmCommands.SetLevel, new[] { new KeyValuePair<string, string>("level", "9") });
        player.Pump(() => answer is not null);
        Assert.Equal(42u, answer!.RequestId);
        Assert.Equal(GmResultCode.Unauthorized, answer.Code);
        player.Realm.PumpFor(new[] { player.Client }, 200);
        Assert.Equal(1, player.Client.Self.Values.Level);
        Assert.Equal(0, player.Client.GmStatus.GmLevel);
    }

    [Fact]
    public async Task God_mode_blocks_damage_until_it_is_turned_off()
    {
        var (realm, gm) = await StartAsync(gm: true);
        await using var _ = realm;
        using var __ = gm;
        gm.Client.SetTarget(gm.Client.LocalGuid);

        Assert.Equal(GmResultCode.Ok, gm.Run(GmCommands.GodMode, ("enabled", "true")).Code);
        gm.Pump(() => gm.Client.GmStatus.Overrides.HasFlag(GmOverrides.GodMode));
        int full = gm.Client.Self.Values.MaxHealth;
        gm.Command(".damage 30");
        gm.Realm.PumpFor(new[] { gm.Client }, 200);
        Assert.Equal(full, gm.Client.Self.Values.Health);

        Assert.Equal(GmResultCode.Ok, gm.Run(GmCommands.GodMode, ("enabled", "false")).Code);
        gm.Pump(() => !gm.Client.GmStatus.Overrides.HasFlag(GmOverrides.GodMode));
        gm.Command(".damage 30");
        gm.Pump(() => gm.Client.Self.Values.Health == full - 30);

        // The console's toggle reaches the menu's state too.
        gm.Command(".god");
        gm.Pump(() => gm.Client.GmStatus.Overrides.HasFlag(GmOverrides.GodMode));
    }

    [Fact]
    public async Task Player_commands_use_the_game_rules_and_refuse_bad_input()
    {
        var (realm, gm) = await StartAsync(gm: true);
        await using var _ = realm;
        using var __ = gm;

        var level = gm.Run(GmCommands.SetLevel, ("level", "5"));
        Assert.Equal(GmResultCode.Ok, level.Code);
        gm.Pump(() => gm.Client.Self.Values.Level == 5);
        // Within the schema's bounds but above the realm's cap: the server says so.
        var tooHigh = gm.Run(GmCommands.SetLevel, ("level", "60"));
        Assert.Equal(GmResultCode.InvalidArgument, tooHigh.Code);
        Assert.Contains("1 to 10", tooHigh.Message);
        // Outside the schema: refused before sending, with the same code.
        Assert.Equal(GmResultCode.InvalidArgument, gm.Run(GmCommands.SetLevel, ("level", "0")).Code);
        Assert.Equal(GmResultCode.InvalidArgument, gm.Run(GmCommands.SetLevel, ("level", "five")).Code);
        Assert.Equal(GmResultCode.InvalidArgument, gm.Run(GmCommands.SetLevel).Code);
        Assert.Equal(GmResultCode.InvalidArgument, gm.Run(GmCommands.AddGold, ("copper", "-5")).Code);
        Assert.Equal(GmResultCode.InvalidArgument, gm.Run(GmCommands.TeleportCoordinates, ("x", "NaN"), ("z", "0")).Code);
        Assert.Equal(GmResultCode.UnknownCommand, gm.Run("player.make_coffee").Code);

        var xp = gm.Run(GmCommands.AddXp, ("amount", "40"));
        Assert.Equal(GmResultCode.Ok, xp.Code);
        gm.Pump(() => gm.Client.Stats.Xp == 40);

        var gold = gm.Run(GmCommands.AddGold, ("copper", "12345"));
        Assert.Equal(GmResultCode.Ok, gold.Code);
        Assert.Equal("12345", gm.Data(gold, "money"));
        gm.Pump(() => gm.Client.Inventory.Money == 12345);

        Assert.Equal(GmResultCode.NotAvailable, gm.Run(GmCommands.Revive).Code); // not dead
        gm.Client.SetTarget(gm.Client.LocalGuid);
        gm.Command(".damage 100000");
        gm.Pump(() => gm.Client.Self.Values.IsDead);
        Assert.Equal(GmResultCode.NotAvailable, gm.Run(GmCommands.HealFull).Code);
        Assert.Equal(GmResultCode.Ok, gm.Run(GmCommands.Revive).Code);
        gm.Pump(() => !gm.Client.Self.Values.IsDead && gm.Client.Self.Values.Health == gm.Client.Self.Values.MaxHealth);

        // A warrior's rage is not "restored".
        Assert.Equal(GmResultCode.NotAvailable, gm.Run(GmCommands.RestoreResources).Code);
    }

    [Fact]
    public async Task Items_go_through_the_bag_rules_and_report_partial_results()
    {
        var (realm, gm) = await StartAsync(gm: true);
        await using var _ = realm;
        using var __ = gm;

        Assert.Equal(GmResultCode.InvalidArgument, gm.Run(GmCommands.AddItem, ("item", "999999")).Code);
        var one = gm.Run(GmCommands.AddItem, ("item", PickHammer.ToString()));
        Assert.Equal(GmResultCode.Ok, one.Code);
        gm.Pump(() => gm.Client.Inventory.Count(PickHammer) == 1);

        var many = gm.Run(GmCommands.AddItem, ("item", PickHammer.ToString()), ("count", "250"));
        Assert.Equal(GmResultCode.Partial, many.Code);
        int added = int.Parse(gm.Data(many, "added"));
        Assert.InRange(added, 1, 249);
        gm.Pump(() => gm.Client.Inventory.Count(PickHammer) == 1 + added);

        var none = gm.Run(GmCommands.AddItem, ("item", PickHammer.ToString()));
        Assert.Equal(GmResultCode.NotAvailable, none.Code);
    }

    [Fact]
    public async Task Debug_spawns_are_tracked_and_only_they_are_removed()
    {
        var (realm, gm) = await StartAsync(true, At(MireCroaker, 0, 30));
        await using var _ = realm;
        using var __ = gm;
        gm.Pump(() => gm.Client.World.Entities.Any(e => e.Name == "Mire Croaker"));

        Assert.Equal(GmResultCode.InvalidArgument, gm.Run(GmCommands.SpawnEntity, ("creature", "999999")).Code);
        Assert.Equal(GmResultCode.InvalidArgument, gm.Run(GmCommands.SpawnEntity, ("creature", DuskWolf.ToString()), ("count", "11")).Code);

        var spawn = gm.Run(GmCommands.SpawnEntity, ("creature", DuskWolf.ToString()), ("count", "3"));
        Assert.Equal(GmResultCode.Ok, spawn.Code);
        gm.Pump(() => gm.Client.World.Entities.Count(e => e.Name == "Dusk Wolf") == 3 && gm.Client.GmStatus.DebugSpawns == 3);

        for (int i = 0; i < 3; i++) Assert.Equal(GmResultCode.Ok, gm.Run(GmCommands.SpawnEntity, ("creature", DuskWolf.ToString()), ("count", "10")).Code);
        var cap = gm.Run(GmCommands.SpawnEntity, ("creature", DuskWolf.ToString()), ("count", "10"));
        Assert.Equal(GmResultCode.InvalidArgument, cap.Code);
        Assert.Contains("At most 40", cap.Message);

        var despawn = gm.Run(GmCommands.DespawnDebug);
        Assert.Equal(GmResultCode.Ok, despawn.Code);
        Assert.Equal("33", gm.Data(despawn, "removed"));
        gm.Pump(() => !gm.Client.World.Entities.Any(e => e.Name == "Dusk Wolf") && gm.Client.GmStatus.DebugSpawns == 0);
        Assert.Contains(gm.Client.World.Entities, e => e.Name == "Mire Croaker");
    }

    [Fact]
    public async Task Debug_spawns_leave_with_the_gm()
    {
        await using var realm = await TestRealm.StartAsync(content: TestRealm.CombatContent(), gmAll: true);
        var (aliceAccount, aliceCharacter) = await realm.NewPlayerAsync("alice", "Alice");
        var (bobAccount, bobCharacter) = await realm.NewPlayerAsync("bob", "Bob");
        var alice = new Gm(realm, realm.Enter(aliceAccount, aliceCharacter));
        using var bob = realm.Enter(bobAccount, bobCharacter);
        var both = new[] { alice.Client, bob };
        realm.Pump(both, () => alice.Client.GmStatus.GmLevel > 0 && bob.State == ClientState.InWorld);

        Assert.Equal(GmResultCode.Ok, alice.Run(GmCommands.SpawnEntity, ("creature", DuskWolf.ToString()), ("count", "2")).Code);
        realm.Pump(both, () => bob.World.Entities.Count(e => e.Name == "Dusk Wolf") == 2);
        alice.Client.Disconnect();
        alice.Dispose();
        realm.Pump(new[] { bob }, () => !bob.World.Entities.Any(e => e.Name == "Dusk Wolf"));
    }

    [Fact]
    public async Task Kill_and_inspect_need_a_fitting_target()
    {
        var (realm, gm) = await StartAsync(true, At(MireCroaker, 0, 30));
        await using var _ = realm;
        using var __ = gm;
        Assert.Equal(GmResultCode.NoTarget, gm.Run(GmCommands.KillTarget).Code);
        Assert.Equal(GmResultCode.NoTarget, gm.Run(GmCommands.InspectTarget).Code);

        RemoteEntity? croaker = null;
        gm.Pump(() => (croaker = gm.Client.World.Entities.FirstOrDefault(e => e.Name == "Mire Croaker")) is not null);
        gm.Client.SetTarget(croaker!.Guid);
        gm.Realm.PumpFor(new[] { gm.Client }, 100);

        var inspect = gm.Run(GmCommands.InspectTarget);
        Assert.Equal(GmResultCode.Ok, inspect.Code);
        Assert.Equal(MireCroaker.ToString(), gm.Data(inspect, "creature"));
        Assert.Equal("Idle", gm.Data(inspect, "ai"));
        Assert.Equal("spawn point", gm.Data(inspect, "spawn"));

        Assert.Equal(GmResultCode.Ok, gm.Run(GmCommands.KillTarget).Code);
        gm.Pump(() => croaker.Unit!.Values.IsDead);
        Assert.Equal(GmResultCode.NoTarget, gm.Run(GmCommands.KillTarget).Code); // dead now
    }

    [Fact]
    public async Task Teleports_go_to_catalog_destinations_and_stay_inside_the_map()
    {
        var (realm, gm) = await StartAsync(true, At(MireCroaker, 120, 80));
        await using var _ = realm;
        using var __ = gm;
        var destination = gm.Client.GmCatalog.Destinations.Single(d => d.Id == "creature:" + MireCroaker);
        Assert.Equal(GmDestinationKind.Creature, destination.Kind);
        Assert.Contains(gm.Client.GmCatalog.Destinations, d => d.Id == "start");
        Assert.Contains(gm.Client.GmCatalog.Creatures, c => c.Id == DuskWolf && c.Reaction != Reaction.Friendly);

        var teleport = gm.Run(GmCommands.TeleportLocation, ("location", destination.Id));
        Assert.Equal(GmResultCode.Ok, teleport.Code);
        gm.Pump(() => MathF.Abs(gm.Client.LocalMovement.Position.X - destination.X) < 2f && MathF.Abs(gm.Client.LocalMovement.Position.Z - destination.Z) < 2f);

        Assert.Equal(GmResultCode.InvalidArgument, gm.Run(GmCommands.TeleportLocation, ("location", "npc:424242")).Code);
        Assert.Equal(GmResultCode.InvalidArgument, gm.Run(GmCommands.TeleportCoordinates, ("x", "5000"), ("z", "0")).Code);
        var coordinates = gm.Run(GmCommands.TeleportCoordinates, ("x", "-40.5"), ("z", "25"));
        Assert.Equal(GmResultCode.Ok, coordinates.Code);
        gm.Pump(() => MathF.Abs(gm.Client.LocalMovement.Position.X + 40.5f) < 1f);
    }

    [Fact]
    public async Task Overrides_show_in_the_status_and_reset_together()
    {
        var (realm, gm) = await StartAsync(gm: true);
        await using var _ = realm;
        using var __ = gm;
        Assert.Equal(GmResultCode.Ok, gm.Run(GmCommands.GodMode, ("enabled", "true")).Code);
        Assert.Equal(GmResultCode.Ok, gm.Run(GmCommands.NoCooldown, ("enabled", "true")).Code);
        Assert.Equal(GmResultCode.Ok, gm.Run(GmCommands.SpeedMultiplier, ("percent", "200")).Code);
        Assert.Equal(GmResultCode.Ok, gm.Run(GmCommands.DamageMultiplier, ("percent", "300")).Code);
        gm.Pump(() => gm.Client.GmStatus.Overrides == (GmOverrides.GodMode | GmOverrides.NoCooldowns)
                      && gm.Client.GmStatus.SpeedPercent == 200 && gm.Client.GmStatus.DamagePercent == 300);
        gm.Pump(() => gm.Client.Self.Values.SpeedPercent == 200);

        Assert.Equal(GmResultCode.Ok, gm.Run(GmCommands.ResetOverrides).Code);
        gm.Pump(() => gm.Client.GmStatus.Overrides == GmOverrides.None && gm.Client.GmStatus.SpeedPercent == 100 && gm.Client.GmStatus.DamagePercent == 100);
        gm.Pump(() => gm.Client.Self.Values.SpeedPercent == 100);
    }

    [Fact]
    public async Task Overlay_geometry_comes_from_server_data()
    {
        var (realm, gm) = await StartAsync(true, At(MireCroaker, 10, 10), At(DuskWolf, -12, 5), At(DuskWolf, 200, 200));
        await using var _ = realm;
        using var __ = gm;

        var navmesh = gm.Run(GmCommands.DebugGeometry, ("kind", GmCommands.GeometryNavMesh), ("radius", "30"));
        Assert.Equal(GmResultCode.Ok, navmesh.Code);
        gm.Pump(() => gm.Geometry.Any(g => g.Kind == GmGeometryKind.NavMesh));
        Assert.True(gm.Geometry.Single(g => g.Kind == GmGeometryKind.NavMesh).Segments > 0);

        Assert.Equal(GmResultCode.Ok, gm.Run(GmCommands.DebugGeometry, ("kind", GmCommands.GeometrySpawnPoints)).Code);
        gm.Pump(() => gm.Geometry.Any(g => g.Kind == GmGeometryKind.SpawnPoints));
        Assert.Equal(6, gm.Geometry.Single(g => g.Kind == GmGeometryKind.SpawnPoints).Segments); // two spawn points in reach, 3 lines each

        Assert.Equal(GmResultCode.Ok, gm.Run(GmCommands.DebugGeometry, ("kind", GmCommands.GeometryCollision)).Code); // flat map: no obstacles
        Assert.Equal(GmResultCode.InvalidArgument, gm.Run(GmCommands.DebugGeometry, ("kind", "ghosts")).Code);
    }

    [Fact]
    public async Task Client_side_commands_and_busy_requests_use_the_same_results()
    {
        var (realm, gm) = await StartAsync(gm: true);
        await using var _ = realm;
        using var __ = gm;

        // Not registered in this client: refused, not simulated.
        Assert.Equal(GmResultCode.NotAvailable, gm.Run(GmCommands.ShowCollision, ("enabled", "true")).Code);
        bool shown = false;
        gm.Client.Gm.RegisterLocal(GmCommands.ShowFps, args =>
        {
            shown = args.Bool("enabled");
            return GmCommandClient.Local(GmCommands.ShowFps, GmResultCode.Ok, "FPS shown.");
        });
        Assert.Equal(GmResultCode.Ok, gm.Run(GmCommands.ShowFps, ("enabled", "true")).Code);
        Assert.True(shown);
        Assert.Equal(GmResultCode.InvalidArgument, gm.Run(GmCommands.ShowFps, ("enabled", "maybe")).Code);

        // A second request for a command still in flight is refused at once.
        GmCommandResult? first = null, second = null;
        gm.Client.Gm.Execute(GmCommands.HealFull, null, r => first = r);
        gm.Client.Gm.Execute(GmCommands.HealFull, null, r => second = r);
        Assert.Equal(GmResultCode.Busy, second!.Code);
        gm.Pump(() => first is not null);
        Assert.Equal(GmResultCode.Ok, first!.Code);
        Assert.Contains(gm.Client.Gm.History, r => r.Code == GmResultCode.Busy);
    }

    [Fact]
    public async Task Quest_commands_follow_the_quest_log()
    {
        var (realm, gm) = await StartAsync(gm: true);
        await using var _ = realm;
        using var __ = gm;
        uint quest = gm.Client.Data.Quests.First().Id;

        Assert.Equal(GmResultCode.NotAvailable, gm.Run(GmCommands.QuestComplete, ("quest", quest.ToString())).Code);
        Assert.Equal(GmResultCode.Ok, gm.Run(GmCommands.QuestStart, ("quest", quest.ToString())).Code);
        gm.Pump(() => gm.Client.Quests.Find(quest) is not null);
        Assert.Equal(GmResultCode.NotAvailable, gm.Run(GmCommands.QuestStart, ("quest", quest.ToString())).Code);
        Assert.Equal(GmResultCode.Ok, gm.Run(GmCommands.QuestComplete, ("quest", quest.ToString())).Code);
        Assert.Equal(GmResultCode.Ok, gm.Run(GmCommands.QuestReset, ("quest", quest.ToString())).Code);
        gm.Pump(() => gm.Client.Quests.Find(quest) is null);
        Assert.Equal(GmResultCode.InvalidArgument, gm.Run(GmCommands.QuestStart, ("quest", "999999")).Code);
    }
}
