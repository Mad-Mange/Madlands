using System.Diagnostics;
using System.Net.Http.Headers;
using Madlands.ClientCore;
using Madlands.Content;
using Madlands.Server.Auth;
using Madlands.Server.Persistence;
using Madlands.Shared;
using Madlands.Shared.Movement;
using Madlands.Shared.Net;
using Madlands.Shared.Terrain;

namespace Madlands.Server.Tests;

/// <summary>
/// A real realm (UDP game server + HTTP account API, wired by <see cref="ServerHost"/> exactly as in
/// production) on loopback with ephemeral ports. The game loop is ticked by the test via <see cref="Pump"/>.
/// </summary>
internal sealed class TestRealm : IAsyncDisposable
{
    /// <summary>Cheap Argon2 parameters: the tests check the flow, not the hash strength.</summary>
    public static readonly PasswordHasher FastHasher = new(memoryKiB: 1024, iterations: 1);

    private TestRealm(ServerHost host)
    {
        Host = host;
    }

    public ServerHost Host { get; }
    public GameServer Game => Host.Game;

    public static ContentDatabase Content()
    {
        var map = new MapDefinition { Id = 1, Name = "Test", Spawn = new SpawnPoint { X = 0, Z = 0 } };
        return new ContentDatabase(
            new[] { new LoadedMap(map, HeightMap.CreateFlat(-200, -200, 4, 101, 101, 10f)) },
            new[] { new RaceDefinition { Id = 1, Name = "Human", StartMap = 1 }, new RaceDefinition { Id = 2, Name = "Korrin", StartMap = 1 } },
            new[] { new ClassDefinition { Id = 1, Name = "Warrior" } });
    }

    public static async Task<TestRealm> StartAsync(IGameDatabase? database = null)
    {
        var options = new ServerOptions { Port = 0, HttpPort = 0, Bind = "127.0.0.1", ApiRateLimitPerMinute = 0 };
        var host = new ServerHost(options, Content(), database ?? new InMemoryGameDatabase(), FastHasher);
        await host.StartAsync();
        return new TestRealm(host);
    }

    public AccountClient Account() => new(Host.HttpBaseUrl);

    /// <summary>Registers an account with one character.</summary>
    public async Task<(AccountClient Account, CharacterSummary Character)> NewPlayerAsync(string username, string characterName)
    {
        var account = Account();
        var auth = await account.RegisterAsync(username, "password-" + username);
        Assert.True(auth.Ok, auth.Message);
        var created = await account.CreateCharacterAsync(characterName, 1, 1);
        Assert.True(created.Ok, created.Message);
        return (account, created.Character);
    }

    public GameClient Enter(AccountClient account, CharacterSummary character)
    {
        var client = new GameClient(manualMode: true);
        client.Connect("127.0.0.1", Game.Port, account.SessionToken, character.Id);
        return client;
    }

    public void Pump(GameClient[] clients, Func<bool> until, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!until())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Condition not reached in time.");
            Game.Tick();
            foreach (var c in clients) c.Update();
            Thread.Sleep(5);
        }
    }

    public void PumpFor(GameClient[] clients, int ms)
    {
        var sw = Stopwatch.StartNew();
        Pump(clients, () => sw.ElapsedMilliseconds >= ms, ms + 1000);
    }

    public ValueTask DisposeAsync() => Host.DisposeAsync();
}

public class WorldIntegrationTests
{
    [Fact]
    public async Task Players_see_each_other_move_and_leave()
    {
        await using var realm = await TestRealm.StartAsync();
        var (aliceAccount, aliceCharacter) = await realm.NewPlayerAsync("alice", "Alice");
        var (bobAccount, bobCharacter) = await realm.NewPlayerAsync("bob", "Bob");

        MovementState aliceSpawn = default;
        using var alice = realm.Enter(aliceAccount, aliceCharacter);
        using var bob = realm.Enter(bobAccount, bobCharacter);
        alice.EnteredWorld += s => aliceSpawn = s;
        var clients = new[] { alice, bob };

        realm.Pump(clients, () => alice.State == ClientState.InWorld && bob.State == ClientState.InWorld);
        Assert.Equal(EntityGuid.Create(EntityType.Player, aliceCharacter.Id), alice.LocalGuid);
        Assert.Equal(10f, aliceSpawn.Position.Y, 3);

        realm.Pump(clients, () => bob.World.TryGet(alice.LocalGuid, out _));
        Assert.Equal("Alice", bob.World.Entities.Single().Name);

        // Alice runs 3 m along +X after enough time has passed for that to be legal.
        realm.PumpFor(clients, 600);
        var moved = new MovementState(aliceSpawn.Position + new Vec3(3, 0, 0), 0, Vec3.Zero, MovementFlags.None);
        alice.SendMovement(moved);
        realm.Pump(clients, () =>
            bob.World.TryGet(alice.LocalGuid, out var e) &&
            Vec3.HorizontalDistance(e.Movement.Newest.Position, moved.Position) < 0.001f);

        alice.Disconnect();
        realm.Pump(new[] { bob }, () => !bob.World.TryGet(alice.LocalGuid, out _));
        Assert.Equal(1, realm.Game.World.PlayerCount);
    }

    [Fact]
    public async Task Impossible_move_is_corrected()
    {
        await using var realm = await TestRealm.StartAsync();
        var (account, character) = await realm.NewPlayerAsync("cheater", "Cheater");
        using var cheater = realm.Enter(account, character);
        var clients = new[] { cheater };

        MovementState spawn = default;
        MovementState? correction = null;
        cheater.EnteredWorld += s => spawn = s;
        cheater.Corrected += s => correction = s;
        realm.Pump(clients, () => cheater.State == ClientState.InWorld);

        cheater.SendMovement(MovementState.Idle(spawn.Position + new Vec3(100, 0, 0), 0));
        realm.Pump(clients, () => correction.HasValue);
        Assert.True(Vec3.HorizontalDistance(correction!.Value.Position, spawn.Position) < 0.01f);
    }

    [Fact]
    public async Task Bad_session_token_is_rejected()
    {
        await using var realm = await TestRealm.StartAsync();
        var (_, character) = await realm.NewPlayerAsync("victim", "Victim");
        using var client = new GameClient(manualMode: true);
        string? reason = null;
        client.Disconnected += r => reason = r;
        client.Connect("127.0.0.1", realm.Game.Port, "forged-token", character.Id);
        realm.Pump(new[] { client }, () => reason is not null);
        Assert.Equal(RejectReason.InvalidSession, client.LastRejectReason);
    }

    [Fact]
    public async Task Cannot_play_another_accounts_character()
    {
        await using var realm = await TestRealm.StartAsync();
        var (_, aliceCharacter) = await realm.NewPlayerAsync("alice", "Alice");
        var (mallory, _) = await realm.NewPlayerAsync("mallory", "Mallory");
        using var client = realm.Enter(mallory, aliceCharacter);
        string? reason = null;
        client.Disconnected += r => reason = r;
        realm.Pump(new[] { client }, () => reason is not null);
        Assert.Equal(RejectReason.CharacterNotFound, client.LastRejectReason);
    }

    [Fact]
    public async Task Second_login_kicks_the_first()
    {
        await using var realm = await TestRealm.StartAsync();
        var (account, character) = await realm.NewPlayerAsync("twice", "Twice");
        using var first = realm.Enter(account, character);
        realm.Pump(new[] { first }, () => first.State == ClientState.InWorld);

        using var second = realm.Enter(account, character);
        realm.Pump(new[] { first, second }, () => second.State == ClientState.InWorld && first.State == ClientState.Disconnected);
        Assert.Equal(RejectReason.LoggedInElsewhere, first.LastRejectReason);
        Assert.Equal(1, realm.Game.World.PlayerCount);
    }

    [Fact]
    public async Task Position_is_saved_on_logout_and_restored_on_login()
    {
        await using var realm = await TestRealm.StartAsync();
        var (account, character) = await realm.NewPlayerAsync("saver", "Saver");
        var (client, target) = MoveInWorld(realm, account, character, new Vec3(4, 0, -3));
        client.Disconnect();
        realm.Pump(Array.Empty<GameClient>(), () => realm.Game.World.PlayerCount == 0);
        client.Dispose();

        await WaitUntilAsync(() => realm.Host.Saver.SavedCount >= 1);
        Assert.True(Vec3.HorizontalDistance(EnterAndGetSpawn(realm, account, character), target) < 0.01f);
    }

    [Fact]
    public async Task Characters_survive_a_server_restart()
    {
        var database = new InMemoryGameDatabase();
        CharacterSummary character;
        Vec3 target;
        GameClient stillOnline;
        await using (var first = await TestRealm.StartAsync(database))
        {
            AccountClient account;
            (account, character) = await first.NewPlayerAsync("persist", "Persist");
            // Shutting down with the player still online must save them.
            (stillOnline, target) = MoveInWorld(first, account, character, new Vec3(-5, 0, 2));
        }
        stillOnline.Dispose();

        await using var second = await TestRealm.StartAsync(database);
        var again = second.Account();
        Assert.True((await again.LoginAsync("persist", "password-persist")).Ok);
        Assert.True(Vec3.HorizontalDistance(EnterAndGetSpawn(second, again, character), target) < 0.01f);
    }

    /// <summary>Enters the world and makes a legal move; returns the still-connected client and the new position.</summary>
    internal static (GameClient Client, Vec3 Target) MoveInWorld(TestRealm realm, AccountClient account, CharacterSummary character, Vec3 offset)
    {
        var client = realm.Enter(account, character);
        MovementState spawn = default;
        client.EnteredWorld += s => spawn = s;
        realm.Pump(new[] { client }, () => client.State == ClientState.InWorld);
        realm.PumpFor(new[] { client }, 1000);

        var target = spawn.Position + offset;
        client.SendMovement(MovementState.Idle(target, 1.5f));
        realm.Pump(new[] { client }, () =>
            realm.Game.World.Instances.Single().Entities.SingleOrDefault() is { } player &&
            Vec3.HorizontalDistance(player.Position, target) < 0.01f);
        return (client, target);
    }

    internal static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Condition not reached in time.");
            await Task.Delay(10);
        }
    }

    internal static Vec3 EnterAndGetSpawn(TestRealm realm, AccountClient account, CharacterSummary character)
    {
        using var client = realm.Enter(account, character);
        MovementState spawn = default;
        client.EnteredWorld += s => spawn = s;
        realm.Pump(new[] { client }, () => client.State == ClientState.InWorld);
        return spawn.Position;
    }
}

public class AccountApiTests
{
    [Fact]
    public async Task Register_login_and_credential_rules()
    {
        await using var realm = await TestRealm.StartAsync();
        var account = realm.Account();

        var registered = await account.RegisterAsync("  Alice ", "correct horse");
        Assert.True(registered.Ok, registered.Message);
        Assert.False(string.IsNullOrEmpty(registered.SessionToken));
        Assert.Equal("127.0.0.1", registered.RealmHost);
        Assert.Equal(realm.Game.Port, registered.RealmPort);

        Assert.Equal(ApiResultCode.UsernameTaken, (await realm.Account().RegisterAsync("ALICE", "another pw")).Result);
        Assert.Equal(ApiResultCode.InvalidUsername, (await realm.Account().RegisterAsync("a b", "password1")).Result);
        Assert.Equal(ApiResultCode.InvalidPassword, (await realm.Account().RegisterAsync("carol", "short")).Result);

        Assert.Equal(ApiResultCode.InvalidCredentials, (await realm.Account().LoginAsync("alice", "wrong password")).Result);
        Assert.Equal(ApiResultCode.InvalidCredentials, (await realm.Account().LoginAsync("nobody", "whatever1")).Result);
        var login = await realm.Account().LoginAsync("alice", "correct horse");
        Assert.True(login.Ok);
        Assert.NotEqual(registered.SessionToken, login.SessionToken);
    }

    [Fact]
    public async Task Repeated_wrong_passwords_are_rate_limited()
    {
        await using var realm = await TestRealm.StartAsync();
        Assert.True((await realm.Account().RegisterAsync("target", "the-real-password")).Ok);
        for (int i = 0; i < 5; i++)
            Assert.Equal(ApiResultCode.InvalidCredentials, (await realm.Account().LoginAsync("target", "guess" + i + "xyz")).Result);
        Assert.Equal(ApiResultCode.RateLimited, (await realm.Account().LoginAsync("target", "the-real-password")).Result);
    }

    [Fact]
    public async Task Character_create_list_and_delete()
    {
        await using var realm = await TestRealm.StartAsync();
        var (account, alice) = await realm.NewPlayerAsync("alice", "aLiCe");
        Assert.Equal("Alice", alice.Name);
        Assert.Equal(1, alice.Level);
        Assert.Equal(1, alice.MapId);

        var other = realm.Account();
        Assert.True((await other.RegisterAsync("bob", "password-bob")).Ok);
        Assert.Equal(ApiResultCode.NameTaken, (await other.CreateCharacterAsync("ALICE", 1, 1)).Result);
        Assert.Equal(ApiResultCode.InvalidName, (await other.CreateCharacterAsync("B0b", 1, 1)).Result);
        Assert.Equal(ApiResultCode.InvalidRequest, (await other.CreateCharacterAsync("Bob", 99, 1)).Result);

        var list = await account.GetCharactersAsync();
        Assert.True(list.Ok);
        Assert.Equal(alice, list.Characters.Single());
        Assert.Equal(new[] { "Human", "Korrin" }, list.Races.Select(r => r.Name));
        Assert.Equal("Warrior", list.Classes.Single().Name);

        // Bob cannot delete Alice's character; Alice can, which frees the name.
        Assert.Equal(ApiResultCode.NotFound, (await other.DeleteCharacterAsync(alice.Id)).Result);
        Assert.True((await account.DeleteCharacterAsync(alice.Id)).Ok);
        Assert.Empty((await account.GetCharactersAsync()).Characters);
        Assert.True((await other.CreateCharacterAsync("Alice", 2, 1)).Ok);
    }

    [Fact]
    public async Task Character_limit_is_enforced()
    {
        await using var realm = await TestRealm.StartAsync();
        var account = realm.Account();
        Assert.True((await account.RegisterAsync("collector", "password-x")).Ok);
        for (int i = 0; i < Api.AccountApi.MaxCharactersPerAccount; i++)
            Assert.True((await account.CreateCharacterAsync("Hero" + (char)('a' + i), 1, 1)).Ok);
        Assert.Equal(ApiResultCode.TooManyCharacters, (await account.CreateCharacterAsync("Onetoomany", 1, 1)).Result);
    }

    [Fact]
    public async Task Online_character_cannot_be_deleted()
    {
        await using var realm = await TestRealm.StartAsync();
        var (account, character) = await realm.NewPlayerAsync("busy", "Busy");
        using var client = realm.Enter(account, character);
        realm.Pump(new[] { client }, () => client.State == ClientState.InWorld);
        Assert.Equal(ApiResultCode.CharacterOnline, (await account.DeleteCharacterAsync(character.Id)).Result);
    }

    [Fact]
    public async Task Requires_session_and_matching_protocol()
    {
        await using var realm = await TestRealm.StartAsync();
        Assert.Equal(ApiResultCode.Unauthorized, (await realm.Account().GetCharactersAsync()).Result);

        var account = realm.Account();
        Assert.True((await account.RegisterAsync("leaver", "password-l")).Ok);
        Assert.True((await account.LogoutAsync()).Ok);
        Assert.Null(account.SessionToken);

        // An old client: raw request with another protocol version.
        using var http = new HttpClient();
        var writer = new PacketWriter();
        Packet.Encode(writer, new LoginRequest { Username = "leaver", Password = "password-l" });
        var request = new HttpRequestMessage(HttpMethod.Post, realm.Host.HttpBaseUrl + "/api/v1/login") { Content = new ByteArrayContent(writer.ToArray()) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Headers.Add(ProtocolVersion.HttpHeader, "1");
        var body = await (await http.SendAsync(request)).Content.ReadAsByteArrayAsync();
        var reader = new PacketReader(body);
        Assert.Equal(Opcode.ApiResult, Packet.ReadOpcode(ref reader));
        var result = new ApiResult();
        result.Read(ref reader);
        Assert.Equal(ApiResultCode.ProtocolMismatch, result.Result);
    }

    [Fact]
    public async Task Unreachable_server_is_reported_not_thrown()
    {
        using var account = new AccountClient("http://127.0.0.1:1");
        var result = await account.LoginAsync("anyone", "password1");
        Assert.Equal(ApiResultCode.Unreachable, result.Result);
    }
}

public class PasswordHasherTests
{
    [Fact]
    public async Task Hashes_verify_and_are_salted()
    {
        var hasher = new PasswordHasher(memoryKiB: 1024, iterations: 1);
        var a = await hasher.HashAsync("correct horse");
        var b = await hasher.HashAsync("correct horse");
        Assert.StartsWith("$argon2id$v=19$m=1024,t=1,p=1$", a);
        Assert.NotEqual(a, b);
        Assert.True(await hasher.VerifyAsync("correct horse", a));
        Assert.False(await hasher.VerifyAsync("wrong horse", a));
        Assert.False(await hasher.VerifyAsync("correct horse", null));
        Assert.False(await hasher.VerifyAsync("correct horse", "$argon2id$garbage"));
    }

    [Fact]
    public async Task Verifies_hashes_made_with_other_parameters()
    {
        var strong = await new PasswordHasher(memoryKiB: 2048, iterations: 2).HashAsync("pw-123456");
        Assert.True(await new PasswordHasher(memoryKiB: 1024, iterations: 1).VerifyAsync("pw-123456", strong));
    }
}

public class ContentLoaderTests
{
    [Fact]
    public void Loads_content_and_reports_all_errors()
    {
        var root = Directory.CreateTempSubdirectory("madlands-content-");
        try
        {
            var good = Directory.CreateDirectory(Path.Combine(root.FullName, "maps", "0001"));
            File.WriteAllText(Path.Combine(good.FullName, "map.json"), """{ "id": 1, "name": "Good", "spawn": { "x": 5, "z": 5 } }""");
            HeightMap.CreateFlat(0, 0, 1, 11, 11, 3f).Save(Path.Combine(good.FullName, "height.bin"));
            File.WriteAllText(Path.Combine(root.FullName, "races.json"), """{ "races": [ { "id": 1, "name": "Human", "startMap": 1 } ] }""");
            File.WriteAllText(Path.Combine(root.FullName, "classes.json"), """{ "classes": [ { "id": 1, "name": "Warrior" } ] }""");

            var db = ContentLoader.Load(root.FullName);
            Assert.Equal(new Vec3(5, 3, 5), db.Maps[1].SpawnPosition);
            Assert.Equal("Human", db.Races[1].Name);
            Assert.Equal("Warrior", db.Classes[1].Name);

            var bad = Directory.CreateDirectory(Path.Combine(root.FullName, "maps", "0002"));
            File.WriteAllText(Path.Combine(bad.FullName, "map.json"), """{ "id": 1, "name": "", "spawn": { "x": 0, "z": 0 } }""");
            File.WriteAllText(Path.Combine(root.FullName, "races.json"), """{ "races": [ { "id": 1, "name": "Human", "startMap": 9 }, { "id": 1, "name": "Dupe", "startMap": 1 } ] }""");
            File.Delete(Path.Combine(root.FullName, "classes.json"));

            var ex = Assert.Throws<ContentException>(() => ContentLoader.Load(root.FullName));
            Assert.Contains(ex.Errors, e => e.Contains("already used"));
            Assert.Contains(ex.Errors, e => e.Contains("name is required"));
            Assert.Contains(ex.Errors, e => e.Contains("height map"));
            Assert.Contains(ex.Errors, e => e.Contains("map 9, which does not exist"));
            Assert.Contains(ex.Errors, e => e.Contains("id 1 is used twice"));
            Assert.Contains(ex.Errors, e => e.Contains("classes.json"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Repository_content_is_valid()
    {
        var root = ContentPaths.FindContentRoot(AppContext.BaseDirectory);
        Assert.NotNull(root);
        var db = ContentLoader.Load(root!);
        Assert.True(db.Maps.ContainsKey(1));
        Assert.NotEmpty(db.Races);
        Assert.NotEmpty(db.Classes);
    }
}
