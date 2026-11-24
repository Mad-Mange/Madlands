using Madlands.ClientCore;
using Madlands.Content;
using Madlands.Navigation;
using Madlands.Shared;
using Madlands.Shared.Movement;
using Madlands.Shared.Net;

namespace Madlands.Server.Tests;

/// <summary>A real-content opening journey, using the actual map and NPC positions without GM commands.</summary>
public class OpeningJourneyTests
{
    [Fact]
    public async Task New_warrior_can_reach_a_real_croaker_and_advance_tamsins_quest()
    {
        var root = ContentPaths.FindContentRoot(AppContext.BaseDirectory);
        Assert.NotNull(root);
        var content = ContentLoader.Load(root!);
        await using var realm = await TestRealm.StartAsync(content: content);
        var (account, character) = await realm.NewPlayerAsync("outdoor", "Outdoor");
        using var client = realm.Enter(account, character);
        var clients = new[] { client };
        realm.Pump(clients, () => client.State == ClientState.InWorld && client.Inventory.All.Any());
        realm.Pump(clients, () => client.World.Entities.Any(e => e.Name == "Old Tamsin"));
        var tamsin = client.World.Entities.First(e => e.Name == "Old Tamsin");
        var map = realm.Game.World.GetInstance(1, 1)!;
        var player = map.Players.Single();
        var terrain = content.Maps[1].HeightMap;
        var corrections = new List<MovementState>();
        client.Corrected += corrections.Add;

        void WalkTo(Vec3 destination)
        {
            var corners = new List<Vec3>();
            Assert.Equal(PathResult.Complete, map.Nav.FindPath(player.Position, destination, corners));
            foreach (var corner in corners.Skip(1))
            {
                var from = player.Position;
                float dx = corner.X - from.X, dz = corner.Z - from.Z;
                int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dz * dz) / 1.5));
                for (int i = 1; i <= steps; i++)
                {
                    realm.PumpFor(clients, 350);
                    float x = from.X + dx * i / steps, z = from.Z + dz * i / steps;
                    client.SendMovement(MovementState.Idle(new Vec3(x, terrain.GetHeight(x, z), z), MathUtil.DirectionToYaw(dx, dz)));
                    realm.PumpFor(clients, 100);
                    Assert.True(Vec3.HorizontalDistance(player.Position, new Vec3(x, player.Position.Y, z)) < 0.5f,
                        $"The route was blocked or rejected near ({x:0.0}, {z:0.0}).");
                }
            }
        }

        WalkTo(new Vec3(-17f, terrain.GetHeight(-17f, -156f), -156f));
        client.Interact(tamsin.Guid);
        realm.Pump(clients, () => client.Gossip.Quests.Any(q => q.QuestId == 1));
        client.AcceptQuest(tamsin.Guid, 1);
        realm.Pump(clients, () => client.Quests.Find(1) is not null);

        // Pick an isolated actual spawn so the test measures one ordinary fight rather than a pack pull.
        var croakers = map.Creatures.Where(c => c.Template.Id == 1).ToArray();
        var target = croakers.Where(c => croakers.All(other => other == c || Vec3.HorizontalDistance(c.Position, other.Position) > 15f))
            .OrderBy(c => Vec3.HorizontalDistance(player.Position, c.Position)).First();
        WalkTo(target.Position);
        for (int i = 0; i < 4 && Vec3.HorizontalDistance(player.Position, target.Position) > 4f; i++) WalkTo(target.Position);
        Assert.True(Vec3.HorizontalDistance(player.Position, target.Position) <= 5f, "The moving croaker could not be reached.");
        realm.Pump(clients, () => client.World.TryGet(target.Guid, out _));
        var targetPosition = target.Position;
        client.SendMovement(MovementState.Idle(player.Position, MathUtil.DirectionToYaw(targetPosition.X - player.Position.X, targetPosition.Z - player.Position.Z)));
        realm.PumpFor(clients, 150);
        client.SetTarget(target.Guid);
        client.CastSpell(1, target.Guid);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (!target.IsDead && !player.IsDead && elapsed.ElapsedMilliseconds < 30000)
        {
            realm.PumpFor(clients, 250);
            if (client.CheckCast(100, target.Guid) == Madlands.Shared.Game.CastResult.Success)
                client.CastSpell(100, target.Guid);
        }
        Assert.False(player.IsDead, "The warrior died during the first isolated croaker fight.");
        Assert.True(target.IsDead, "The first croaker was not defeated in 30 seconds.");
        realm.Pump(clients, () => client.Quests.Find(1)!.Progress[0] >= 1);
        Assert.Empty(corrections);
    }

    [Fact]
    public async Task New_warrior_can_walk_the_village_and_complete_the_first_talk_quest()
    {
        var root = ContentPaths.FindContentRoot(AppContext.BaseDirectory);
        Assert.NotNull(root);
        var content = ContentLoader.Load(root!);
        await using var realm = await TestRealm.StartAsync(content: content);
        var (account, character) = await realm.NewPlayerAsync("journey", "Journey");
        using var client = realm.Enter(account, character);
        var clients = new[] { client };
        var corrections = new List<MovementState>();
        client.Corrected += corrections.Add;
        realm.Pump(clients, () => client.State == ClientState.InWorld && client.Inventory.All.Any());
        realm.Pump(clients, () => client.World.Entities.Any(e => e.Name == "Captain Edda Brand") &&
                                  client.World.Entities.Any(e => e.Name == "Quartermaster Holt"));

        Assert.Contains(1u, client.KnownSpells);
        Assert.Contains(100u, client.KnownSpells);
        Assert.True(client.Inventory.Count(4001) >= 4);
        var edda = client.World.Entities.First(e => e.Name == "Captain Edda Brand");
        var holt = client.World.Entities.First(e => e.Name == "Quartermaster Holt");
        var player = realm.Game.World.GetInstance(1, 1)!.Players.Single();
        var terrain = content.Maps[1].HeightMap;

        void WalkTo(float x, float z)
        {
            var from = player.Position;
            float dx = x - from.X, dz = z - from.Z;
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dz * dz) / 1.5));
            for (int i = 1; i <= steps; i++)
            {
                realm.PumpFor(clients, 350);
                float px = from.X + dx * i / steps, pz = from.Z + dz * i / steps;
                client.SendMovement(MovementState.Idle(new Vec3(px, terrain.GetHeight(px, pz), pz), 0));
                realm.PumpFor(clients, 100);
                Assert.True(Vec3.HorizontalDistance(player.Position, new Vec3(px, player.Position.Y, pz)) < 0.5f,
                    $"The route was blocked or rejected near ({px:0.0}, {pz:0.0}).");
            }
        }

        WalkTo(17f, -159f);
        client.Interact(edda.Guid);
        realm.Pump(clients, () => client.Gossip.Quests.Any(q => q.QuestId == 5));
        client.AcceptQuest(edda.Guid, 5);
        realm.Pump(clients, () => client.Quests.Find(5) is not null);

        WalkTo(13f, -147f);
        client.Interact(holt.Guid);
        realm.Pump(clients, () => client.Quests.Find(5)!.IsComplete);
        WalkTo(17f, -159f);
        client.TurnInQuest(edda.Guid, 5);
        realm.Pump(clients, () => client.Quests.IsCompleted(5) && client.Inventory.Count(4003) == 2);
        Assert.Empty(corrections);
    }
}
