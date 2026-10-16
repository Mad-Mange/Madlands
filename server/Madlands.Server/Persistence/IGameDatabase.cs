namespace Madlands.Server.Persistence;

public sealed record AccountRecord(long Id, string Username, string PasswordHash);

/// <param name="Health">Current health; null = full (a new character).</param>
/// <param name="Power">Current mana/rage; null = the class default.</param>
public sealed record CharacterRecord(
    long Id, long AccountId, string Name, byte Race, byte Class, byte Level,
    ushort MapId, float X, float Y, float Z, float Yaw, int Xp = 0, int? Health = null, int? Power = null);

/// <summary>What the write-behind saver persists for an online character.</summary>
public readonly record struct CharacterSnapshot(
    long CharacterId, ushort MapId, float X, float Y, float Z, float Yaw, byte Level, int Xp, int Health, int Power);

public enum CreateCharacterOutcome
{
    Created,
    NameTaken,
    TooManyCharacters,
}

/// <summary>
/// All persistence the game needs. Implementations: <see cref="PostgresGameDatabase"/> (production)
/// and <see cref="InMemoryGameDatabase"/> (tests, quick local runs). Every method is safe to call
/// concurrently and must never be awaited on the game loop thread.
/// </summary>
public interface IGameDatabase
{
    /// <summary>Returns the new account id, or null if the (normalized) username is taken.</summary>
    Task<long?> CreateAccountAsync(string username, string passwordHash, CancellationToken ct = default);
    Task<AccountRecord?> FindAccountAsync(string username, CancellationToken ct = default);
    Task TouchLoginAsync(long accountId, CancellationToken ct = default);
    /// <summary>0 for players; higher levels may use GM commands.</summary>
    Task<int> GetGmLevelAsync(long accountId, CancellationToken ct = default);
    /// <summary>Admin: grants or revokes GM commands. False if the (normalized) account does not exist.</summary>
    Task<bool> SetGmLevelAsync(string username, int level, CancellationToken ct = default);

    Task CreateSessionAsync(byte[] tokenHash, long accountId, DateTime expiresUtc, CancellationToken ct = default);
    /// <summary>Account id for a valid, unexpired session; null otherwise.</summary>
    Task<long?> FindSessionAccountAsync(byte[] tokenHash, DateTime nowUtc, CancellationToken ct = default);
    Task DeleteSessionAsync(byte[] tokenHash, CancellationToken ct = default);

    Task<IReadOnlyList<CharacterRecord>> ListCharactersAsync(long accountId, CancellationToken ct = default);
    /// <summary>Atomically enforces the per-account limit and realm-wide unique names. Id of <paramref name="template"/> is ignored.</summary>
    Task<(CreateCharacterOutcome Outcome, CharacterRecord? Character)> CreateCharacterAsync(CharacterRecord template, int maxPerAccount, CancellationToken ct = default);
    /// <summary>Soft-deletes (frees the name, keeps the row). False if not found or not owned by the account.</summary>
    Task<bool> DeleteCharacterAsync(long accountId, long characterId, CancellationToken ct = default);
    /// <summary>Null if the character does not exist, is deleted, or belongs to another account.</summary>
    Task<CharacterRecord?> LoadCharacterAsync(long accountId, long characterId, CancellationToken ct = default);
    Task SaveCharacterAsync(CharacterSnapshot snapshot, CancellationToken ct = default);
}
