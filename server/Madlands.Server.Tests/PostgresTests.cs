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
    public async Task Save_updates_position_level_and_combat_state()
    {
        long account = (await Db.CreateAccountAsync("saver", "h"))!.Value;
        var (_, c) = await Db.CreateCharacterAsync(Template(account, "Saver"), 10);
        Assert.Equal((0, (int?)null, (int?)null), (c!.Xp, c.Health, c.Power));
        await Db.SaveCharacterAsync(new CharacterSnapshot(c.Id, 1, -12.5f, 30.25f, 99f, 3.1f, 7, 345, 80, 12));
        var loaded = await Db.LoadCharacterAsync(account, c.Id);
        Assert.Equal((-12.5f, 30.25f, 99f, 3.1f, (byte)7), (loaded!.X, loaded.Y, loaded.Z, loaded.Yaw, loaded.Level));
        Assert.Equal((345, (int?)80, (int?)12), (loaded.Xp, loaded.Health, loaded.Power));
    }

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
