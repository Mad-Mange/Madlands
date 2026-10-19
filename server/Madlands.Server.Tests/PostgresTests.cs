using Madlands.ClientCore;
using Madlands.Server.Persistence;
using Madlands.Shared;
using Madlands.Shared.Net;
using Npgsql;

namespace Madlands.Server.Tests;

/// <summary>
/// Locates a PostgreSQL server for tests: MADLANDS_TEST_PG (an Npgsql connection string to any
/// database the user can create databases from), else the local docker-compose defaults.
/// </summary>
internal static class PostgresTestServer
{
    private static readonly Lazy<string?> UnavailableReason = new(Probe);

    public static string MaintenanceConnectionString =>
        Environment.GetEnvironmentVariable("MADLANDS_TEST_PG")
        ?? "Host=127.0.0.1;Port=5432;Username=madlands;Password=madlands_dev;Database=postgres;Timeout=3";

    public static string? Unavailable => UnavailableReason.Value;

    /// <summary>A fresh, uniquely named database; dropped by <see cref="DropAsync"/>.</summary>
    public static string CreateDatabaseName() => "madlands_test_" + Guid.NewGuid().ToString("N")[..12];

    public static string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(MaintenanceConnectionString) { Database = database }.ConnectionString;

    public static async Task DropAsync(string database)
    {
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(MaintenanceConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string? Probe()
    {
        try
        {
            using var conn = new NpgsqlConnection(MaintenanceConnectionString);
            conn.Open();
            return null;
        }
        catch (Exception ex)
        {
            return "PostgreSQL not available (" + ex.Message + "). Start it with 'docker compose -f deploy/docker-compose.yml up -d' or set MADLANDS_TEST_PG.";
        }
    }
}

/// <summary>A fact that is skipped (not failed) when no PostgreSQL server is reachable.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (PostgresTestServer.Unavailable is { } reason) Skip = reason;
    }
}

/// <summary>Admin operations, checked against both database implementations.</summary>
internal static class AccountAdminChecks
{
    public static async Task SetPassword(IGameDatabase db)
    {
        long id = (await db.CreateAccountAsync("forgetful", "old-hash"))!.Value;
        long other = (await db.CreateAccountAsync("bystander", "other-hash"))!.Value;
        var now = DateTime.UtcNow;
        await db.CreateSessionAsync(new byte[] { 1 }, id, now.AddHours(1));
        await db.CreateSessionAsync(new byte[] { 2 }, other, now.AddHours(1));

        Assert.True(await db.SetPasswordAsync("forgetful", "new-hash"));
        Assert.Equal("new-hash", (await db.FindAccountAsync("forgetful"))!.PasswordHash);
        Assert.Null(await db.FindSessionAccountAsync(new byte[] { 1 }, now));
        Assert.Equal(other, await db.FindSessionAccountAsync(new byte[] { 2 }, now));
        Assert.Equal("other-hash", (await db.FindAccountAsync("bystander"))!.PasswordHash);
        Assert.False(await db.SetPasswordAsync("nobody", "hash"));
    }

    private static CharacterRecord Template(long accountId, string name) => new(0, accountId, name, 1, 1, 1, 1, 0, 0, 0, 0);

    /// <summary>A save writes position, combat state, money, items and quests; the next save replaces them all.</summary>
    public static async Task SaveCharacterState(IGameDatabase db)
    {
        long account = (await db.CreateAccountAsync("saver", "h"))!.Value;
        var (_, c) = await db.CreateCharacterAsync(Template(account, "Saver"), 10);
        Assert.Equal((0, (int?)null, (int?)null, 0L, false), (c!.Xp, c.Health, c.Power, c.Money, c.StarterKit));
        Assert.Empty((await db.LoadCharacterDataAsync(c.Id)).Items);

        var items = new[] { new ItemRecord(0, 14, 1001, 1), new ItemRecord(0, 16, 5001, 1), new ItemRecord(1, 3, 4001, 7), new ItemRecord(2, 5, 2003, 4) };
        var quests = new[] { new QuestRecord(3, false, new[] { 5 }), new QuestRecord(7, false, new[] { 2, 0, 1 }), new QuestRecord(1, true, Array.Empty<int>()) };
        Assert.Null(c.LastSavedUtc);
        await db.SaveCharacterAsync(new CharacterSnapshot(c.Id, 1, -12.5f, 30.25f, 99f, 3.1f, 7, 345, 80, 12, 123_456, true, items, quests, RestedXp: 640));
        var loaded = await db.LoadCharacterAsync(account, c.Id);
        Assert.Equal((-12.5f, 30.25f, 99f, 3.1f, (byte)7), (loaded!.X, loaded.Y, loaded.Z, loaded.Yaw, loaded.Level));
        Assert.Equal((345, (int?)80, (int?)12, 123_456L, true), (loaded.Xp, loaded.Health, loaded.Power, loaded.Money, loaded.StarterKit));
        // Rested experience, and when it was saved (the time away is measured from it).
        Assert.Equal(640, loaded.RestedXp);
        Assert.InRange(loaded.LastSavedUtc!.Value, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(5));

        var data = await db.LoadCharacterDataAsync(c.Id);
        Assert.Equal(items.OrderBy(i => i.Container).ThenBy(i => i.Slot), data.Items.OrderBy(i => i.Container).ThenBy(i => i.Slot));
        Assert.Equal(quests.Select(q => (q.QuestId, q.Rewarded, string.Join(",", q.Progress))).OrderBy(q => q.QuestId),
            data.Quests.Select(q => (q.QuestId, q.Rewarded, string.Join(",", q.Progress))).OrderBy(q => q.QuestId));

        await db.SaveCharacterAsync(new CharacterSnapshot(c.Id, 1, 0, 0, 0, 0, 7, 345, 80, 12, 5, true, new[] { new ItemRecord(1, 0, 1002, 1) }, Array.Empty<QuestRecord>()));
        data = await db.LoadCharacterDataAsync(c.Id);
        Assert.Equal(new[] { new ItemRecord(1, 0, 1002, 1) }, data.Items);
        Assert.Empty(data.Quests);
        Assert.Equal(5L, (await db.LoadCharacterAsync(account, c.Id))!.Money);
    }
}

public class InMemoryDatabaseTests
{
    [Fact]
    public Task Set_password_replaces_the_hash_and_ends_sessions() => AccountAdminChecks.SetPassword(new InMemoryGameDatabase());

    [Fact]
    public Task Save_and_load_character_state_items_and_quests() => AccountAdminChecks.SaveCharacterState(new InMemoryGameDatabase());
}

public class PostgresDatabaseTests : IAsyncLifetime
{
    private readonly string _databaseName = PostgresTestServer.CreateDatabaseName();
    private PostgresGameDatabase? _db;

    private PostgresGameDatabase Db => _db ??= PostgresGameDatabase.Open(PostgresTestServer.ConnectionStringFor(_databaseName));

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_db is null) return;
        await _db.DisposeAsync();
        await PostgresTestServer.DropAsync(_databaseName);
    }

    private static CharacterRecord Template(long accountId, string name) =>
        new(0, accountId, name, Race: 1, Class: 2, Level: 1, MapId: 1, X: 1, Y: 2, Z: 3, Yaw: 0.5f);

    [PostgresFact]
    public async Task Accounts_and_sessions()
    {
        var id = await Db.CreateAccountAsync("alice", "hash-a");
        Assert.NotNull(id);
        Assert.Null(await Db.CreateAccountAsync("alice", "hash-b"));
        Assert.Equal(new AccountRecord(id.Value, "alice", "hash-a"), await Db.FindAccountAsync("alice"));
        Assert.Null(await Db.FindAccountAsync("nobody"));
        await Db.TouchLoginAsync(id.Value);

        var token = new byte[] { 1, 2, 3, 4 };
        var now = DateTime.UtcNow;
        await Db.CreateSessionAsync(token, id.Value, now.AddHours(1));
        Assert.Equal(id, await Db.FindSessionAccountAsync(token, now));
        Assert.Null(await Db.FindSessionAccountAsync(token, now.AddHours(2))); // expired
        await Db.DeleteSessionAsync(token);
        Assert.Null(await Db.FindSessionAccountAsync(token, now));
    }

    [PostgresFact]
    public async Task Characters_names_limits_and_soft_delete()
    {
        long alice = (await Db.CreateAccountAsync("alice", "h"))!.Value;
        long bob = (await Db.CreateAccountAsync("bob", "h"))!.Value;

        var (outcome, created) = await Db.CreateCharacterAsync(Template(alice, "Hero"), maxPerAccount: 2);
        Assert.Equal(CreateCharacterOutcome.Created, outcome);
        Assert.Equal(Template(alice, "Hero") with { Id = created!.Id }, await Db.LoadCharacterAsync(alice, created.Id));

        // Names are unique per realm, case-insensitively, across accounts.
        Assert.Equal(CreateCharacterOutcome.NameTaken, (await Db.CreateCharacterAsync(Template(bob, "HERO"), 2)).Outcome);
        Assert.Equal(CreateCharacterOutcome.Created, (await Db.CreateCharacterAsync(Template(alice, "Second"), 2)).Outcome);
        Assert.Equal(CreateCharacterOutcome.TooManyCharacters, (await Db.CreateCharacterAsync(Template(alice, "Third"), 2)).Outcome);
        Assert.Equal(new[] { "Hero", "Second" }, (await Db.ListCharactersAsync(alice)).Select(c => c.Name));

        // Other accounts can neither load nor delete it; deleting frees the name.
        Assert.Null(await Db.LoadCharacterAsync(bob, created.Id));
        Assert.False(await Db.DeleteCharacterAsync(bob, created.Id));
        Assert.True(await Db.DeleteCharacterAsync(alice, created.Id));
        Assert.Null(await Db.LoadCharacterAsync(alice, created.Id));
        Assert.Equal(CreateCharacterOutcome.Created, (await Db.CreateCharacterAsync(Template(bob, "Hero"), 2)).Outcome);
    }

    [PostgresFact]
    public Task Save_and_load_character_state_items_and_quests() => AccountAdminChecks.SaveCharacterState(Db);

    [PostgresFact]
    public Task Set_password_replaces_the_hash_and_ends_sessions() => AccountAdminChecks.SetPassword(Db);

    [PostgresFact]
    public Task Expired_sessions_are_deleted() => SessionCleanupChecks.Run(Db);

    [PostgresFact]
    public async Task Gm_level_defaults_to_zero()
    {
        long account = (await Db.CreateAccountAsync("plainuser", "h"))!.Value;
        Assert.Equal(0, await Db.GetGmLevelAsync(account));
    }

    [PostgresFact]
    public async Task Full_realm_on_postgres_survives_restart()
    {
        CharacterSummary character;
        Vec3 target;
        GameClient stillOnline;
        await using (var first = await TestRealm.StartAsync(Db))
        {
            AccountClient account;
            (account, character) = await first.NewPlayerAsync("persist", "Persist");
            (stillOnline, target) = WorldIntegrationTests.MoveInWorld(first, account, character, new Vec3(3, 0, 3));
        }
        stillOnline.Dispose();

        await using var second = await TestRealm.StartAsync(Db);
        var again = second.Account();
        Assert.True((await again.LoginAsync("persist", "password-persist")).Ok);
        var spawn = WorldIntegrationTests.EnterAndGetSpawn(second, again, character);
        Assert.True(Vec3.HorizontalDistance(spawn, target) < 0.01f);
    }
}
