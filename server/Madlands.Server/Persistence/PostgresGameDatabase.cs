using Dapper;
using DbUp;
using DbUp.Engine.Output;
using Npgsql;

namespace Madlands.Server.Persistence;

/// <summary>
/// PostgreSQL via Npgsql + Dapper with explicit SQL. <see cref="Open"/> creates the database if it
/// does not exist and applies pending migrations (Persistence/Migrations/*.sql, embedded).
/// </summary>
public sealed class PostgresGameDatabase : IGameDatabase, IAsyncDisposable
{
    private const string CharacterColumns =
        "id, account_id, name, race, class, level, map_id, pos_x, pos_y, pos_z, yaw, xp, health, power, money, starter_kit, rested_xp, last_saved_at";

    private readonly NpgsqlDataSource _dataSource;

    static PostgresGameDatabase()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    private PostgresGameDatabase(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public static PostgresGameDatabase Open(string connectionString, Action<string>? log = null)
    {
        EnsureDatabase.For.PostgresqlDatabase(connectionString, new NoOpUpgradeLog());
        var upgrader = DeployChanges.To.PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(PostgresGameDatabase).Assembly, name => name.Contains(".Persistence.Migrations."))
            .WithTransactionPerScript()
            .LogTo(new NoOpUpgradeLog())
            .Build();
        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
            throw new InvalidOperationException($"Database migration failed in {result.ErrorScript?.Name}: {result.Error?.Message}", result.Error);
        foreach (var script in result.Scripts) log?.Invoke($"Applied migration {script.Name}");
        return new PostgresGameDatabase(NpgsqlDataSource.Create(connectionString));
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    // ------------------------------------------------------------------ accounts and sessions

    public async Task<long?> CreateAccountAsync(string username, string passwordHash, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            "INSERT INTO accounts (username, password_hash) VALUES (@username, @passwordHash) ON CONFLICT (username) DO NOTHING RETURNING id",
            new { username, passwordHash }, cancellationToken: ct));
    }

    public async Task<AccountRecord?> FindAccountAsync(string username, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<AccountRow>(new CommandDefinition(
            "SELECT id, username, password_hash FROM accounts WHERE username = @username", new { username }, cancellationToken: ct));
        return row is null ? null : new AccountRecord(row.Id, row.Username, row.PasswordHash);
    }

    public async Task TouchLoginAsync(long accountId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE accounts SET last_login_at = now() WHERE id = @accountId", new { accountId }, cancellationToken: ct));
    }

    public async Task<int> GetGmLevelAsync(long accountId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<short>(new CommandDefinition(
            "SELECT gm_level FROM accounts WHERE id = @accountId", new { accountId }, cancellationToken: ct));
    }

    public async Task<bool> SetGmLevelAsync(string username, int level, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        int rows = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE accounts SET gm_level = @level WHERE username = @username", new { username, level = (short)level }, cancellationToken: ct));
        return rows == 1;
    }

    public async Task<bool> SetPasswordAsync(string username, string passwordHash, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        long rows = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            WITH updated AS (UPDATE accounts SET password_hash = @passwordHash WHERE username = @username RETURNING id),
                 ended AS (DELETE FROM sessions WHERE account_id IN (SELECT id FROM updated))
            SELECT count(*) FROM updated
            """,
            new { username, passwordHash }, cancellationToken: ct));
        return rows == 1;
    }

    public async Task CreateSessionAsync(byte[] tokenHash, long accountId, DateTime expiresUtc, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO sessions (token_hash, account_id, expires_at) VALUES (@tokenHash, @accountId, @expiresUtc)",
            new { tokenHash, accountId, expiresUtc }, cancellationToken: ct));
    }

    public async Task<long?> FindSessionAccountAsync(byte[] tokenHash, DateTime nowUtc, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT account_id FROM sessions WHERE token_hash = @tokenHash AND expires_at > @nowUtc",
            new { tokenHash, nowUtc }, cancellationToken: ct));
    }

    public async Task DeleteSessionAsync(byte[] tokenHash, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM sessions WHERE token_hash = @tokenHash", new { tokenHash }, cancellationToken: ct));
    }

    public async Task<int> DeleteExpiredSessionsAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM sessions WHERE expires_at <= @nowUtc", new { nowUtc }, cancellationToken: ct));
    }

    // ------------------------------------------------------------------ characters

    public async Task<IReadOnlyList<CharacterRecord>> ListCharactersAsync(long accountId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<CharacterRow>(new CommandDefinition(
            $"SELECT {CharacterColumns} FROM characters WHERE account_id = @accountId AND deleted_at IS NULL ORDER BY id",
            new { accountId }, cancellationToken: ct));
        return rows.Select(r => r.ToRecord()).ToList();
    }

    public async Task<(CreateCharacterOutcome Outcome, CharacterRecord? Character)> CreateCharacterAsync(CharacterRecord template, int maxPerAccount, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Lock the account row so two concurrent creations cannot both pass the limit check.
        await conn.ExecuteAsync(new CommandDefinition(
            "SELECT 1 FROM accounts WHERE id = @AccountId FOR UPDATE", new { template.AccountId }, tx, cancellationToken: ct));
        long count = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM characters WHERE account_id = @AccountId AND deleted_at IS NULL", new { template.AccountId }, tx, cancellationToken: ct));
        if (count >= maxPerAccount) return (CreateCharacterOutcome.TooManyCharacters, null);

        try
        {
            long id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                """
                INSERT INTO characters (account_id, name, race, class, level, map_id, pos_x, pos_y, pos_z, yaw)
                VALUES (@AccountId, @Name, @Race, @Class, @Level, @MapId, @X, @Y, @Z, @Yaw)
                RETURNING id
                """,
                new
                {
                    template.AccountId, template.Name, Race = (short)template.Race, Class = (short)template.Class,
                    Level = (short)template.Level, MapId = (int)template.MapId, template.X, template.Y, template.Z, template.Yaw,
                },
                tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            return (CreateCharacterOutcome.Created, template with { Id = id });
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return (CreateCharacterOutcome.NameTaken, null);
        }
    }

    public async Task<bool> DeleteCharacterAsync(long accountId, long characterId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        int rows = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE characters SET deleted_at = now() WHERE id = @characterId AND account_id = @accountId AND deleted_at IS NULL",
            new { characterId, accountId }, cancellationToken: ct));
        return rows == 1;
    }

    public async Task<CharacterRecord?> LoadCharacterAsync(long accountId, long characterId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<CharacterRow>(new CommandDefinition(
            $"SELECT {CharacterColumns} FROM characters WHERE id = @characterId AND account_id = @accountId AND deleted_at IS NULL",
            new { characterId, accountId }, cancellationToken: ct));
        return row?.ToRecord();
    }

    public async Task<CharacterData> LoadCharacterDataAsync(long characterId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var items = await conn.QueryAsync<ItemRow>(new CommandDefinition(
            "SELECT container, slot, item_id, count FROM character_items WHERE character_id = @characterId ORDER BY container, slot",
            new { characterId }, cancellationToken: ct));
        var quests = await conn.QueryAsync<QuestRow>(new CommandDefinition(
            "SELECT quest_id, rewarded, progress FROM character_quests WHERE character_id = @characterId ORDER BY quest_id",
            new { characterId }, cancellationToken: ct));
        return new CharacterData(
            items.Select(i => new ItemRecord((byte)i.Container, (byte)i.Slot, (uint)i.ItemId, i.Count)).ToList(),
            quests.Select(q => new QuestRecord((uint)q.QuestId, q.Rewarded, q.Progress ?? Array.Empty<int>())).ToList());
    }

    public async Task SaveCharacterAsync(CharacterSnapshot s, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE characters
            SET map_id = @MapId, pos_x = @X, pos_y = @Y, pos_z = @Z, yaw = @Yaw, level = @Level,
                xp = @Xp, health = @Health, power = @Power, money = @Money, starter_kit = @StarterKit, rested_xp = @RestedXp,
                last_saved_at = now()
            WHERE id = @CharacterId
            """,
            new { s.CharacterId, MapId = (int)s.MapId, s.X, s.Y, s.Z, s.Yaw, Level = (short)s.Level, s.Xp, s.Health, s.Power, s.Money, s.StarterKit, s.RestedXp },
            tx, cancellationToken: ct));

        // Items and quests are rewritten as a whole: one statement each, whatever their number.
        await conn.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM character_items WHERE character_id = @CharacterId;
            INSERT INTO character_items (character_id, container, slot, item_id, count)
            SELECT @CharacterId, c, sl, i, n FROM unnest(@Containers, @Slots, @ItemIds, @Counts) AS t(c, sl, i, n);
            DELETE FROM character_quests WHERE character_id = @CharacterId;
            INSERT INTO character_quests (character_id, quest_id, rewarded, progress)
            SELECT @CharacterId, q, r, string_to_array(p, ',')::integer[] FROM unnest(@QuestIds, @Rewarded, @Progress) AS t(q, r, p);
            """,
            new
            {
                s.CharacterId,
                Containers = s.Items.Select(i => (short)i.Container).ToArray(),
                Slots = s.Items.Select(i => (short)i.Slot).ToArray(),
                ItemIds = s.Items.Select(i => (long)i.ItemId).ToArray(),
                Counts = s.Items.Select(i => i.Count).ToArray(),
                QuestIds = s.Quests.Select(q => (long)q.QuestId).ToArray(),
                Rewarded = s.Quests.Select(q => q.Rewarded).ToArray(),
                Progress = s.Quests.Select(q => string.Join(',', q.Progress)).ToArray(),
            },
            tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    private sealed class AccountRow
    {
        public long Id { get; set; }
        public string Username { get; set; } = "";
        public string PasswordHash { get; set; } = "";
    }

    private sealed class CharacterRow
    {
        public long Id { get; set; }
        public long AccountId { get; set; }
        public string Name { get; set; } = "";
        public short Race { get; set; }
        public short Class { get; set; }
        public short Level { get; set; }
        public int MapId { get; set; }
        public float PosX { get; set; }
        public float PosY { get; set; }
        public float PosZ { get; set; }
        public float Yaw { get; set; }
        public int Xp { get; set; }
        public int? Health { get; set; }
        public int? Power { get; set; }
        public long Money { get; set; }
        public bool StarterKit { get; set; }
        public int RestedXp { get; set; }
        public DateTime? LastSavedAt { get; set; }

        public CharacterRecord ToRecord() =>
            new(Id, AccountId, Name, (byte)Race, (byte)Class, (byte)Level, (ushort)MapId, PosX, PosY, PosZ, Yaw, Xp, Health, Power, Money, StarterKit,
                RestedXp, LastSavedAt is { } saved ? DateTime.SpecifyKind(saved, DateTimeKind.Utc) : null);
    }

    private sealed class ItemRow
    {
        public short Container { get; set; }
        public short Slot { get; set; }
        public long ItemId { get; set; }
        public int Count { get; set; }
    }

    private sealed class QuestRow
    {
        public long QuestId { get; set; }
        public bool Rewarded { get; set; }
        public int[]? Progress { get; set; }
    }
}
