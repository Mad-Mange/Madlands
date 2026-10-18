namespace Madlands.Server.Persistence;

/// <summary>
/// Process-local database for tests and quick local runs (<c>--db memory</c>). Same semantics as
/// the Postgres implementation, guarded by a single lock. Data is lost when the process exits.
/// </summary>
public sealed class InMemoryGameDatabase : IGameDatabase
{
    private sealed class Session
    {
        public long AccountId;
        public DateTime ExpiresUtc;
    }

    private readonly object _lock = new();
    private readonly Dictionary<long, AccountRecord> _accounts = new();
    private readonly Dictionary<string, Session> _sessions = new();
    private readonly Dictionary<long, CharacterRecord> _characters = new();
    private readonly HashSet<long> _deleted = new();
    private readonly Dictionary<long, int> _gmLevels = new();
    private readonly Dictionary<long, CharacterData> _data = new();
    private long _nextAccountId = 1;
    private long _nextCharacterId = 1;

    public Task<long?> CreateAccountAsync(string username, string passwordHash, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_accounts.Values.Any(a => a.Username == username)) return Task.FromResult<long?>(null);
            var account = new AccountRecord(_nextAccountId++, username, passwordHash);
            _accounts.Add(account.Id, account);
            return Task.FromResult<long?>(account.Id);
        }
    }

    public Task<AccountRecord?> FindAccountAsync(string username, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(_accounts.Values.FirstOrDefault(a => a.Username == username));
    }

    public Task TouchLoginAsync(long accountId, CancellationToken ct = default) => Task.CompletedTask;

    public Task<int> GetGmLevelAsync(long accountId, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(_gmLevels.GetValueOrDefault(accountId));
    }

    public Task<bool> SetGmLevelAsync(string username, int level, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var account = _accounts.Values.FirstOrDefault(a => a.Username == username);
            if (account is null) return Task.FromResult(false);
            _gmLevels[account.Id] = level;
            return Task.FromResult(true);
        }
    }

    public Task<bool> SetPasswordAsync(string username, string passwordHash, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var account = _accounts.Values.FirstOrDefault(a => a.Username == username);
            if (account is null) return Task.FromResult(false);
            _accounts[account.Id] = account with { PasswordHash = passwordHash };
            foreach (var key in _sessions.Where(s => s.Value.AccountId == account.Id).Select(s => s.Key).ToList()) _sessions.Remove(key);
            return Task.FromResult(true);
        }
    }

    /// <summary>Tests: grant GM commands to an account.</summary>
    public void SetGmLevel(long accountId, int level)
    {
        lock (_lock) _gmLevels[accountId] = level;
    }

    public Task CreateSessionAsync(byte[] tokenHash, long accountId, DateTime expiresUtc, CancellationToken ct = default)
    {
        lock (_lock) _sessions[Convert.ToHexString(tokenHash)] = new Session { AccountId = accountId, ExpiresUtc = expiresUtc };
        return Task.CompletedTask;
    }

    public Task<long?> FindSessionAccountAsync(byte[] tokenHash, DateTime nowUtc, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult<long?>(
                _sessions.TryGetValue(Convert.ToHexString(tokenHash), out var s) && s.ExpiresUtc > nowUtc ? s.AccountId : null);
        }
    }

    public Task DeleteSessionAsync(byte[] tokenHash, CancellationToken ct = default)
    {
        lock (_lock) _sessions.Remove(Convert.ToHexString(tokenHash));
        return Task.CompletedTask;
    }

    public Task<int> DeleteExpiredSessionsAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var expired = _sessions.Where(s => s.Value.ExpiresUtc <= nowUtc).Select(s => s.Key).ToList();
            foreach (var key in expired) _sessions.Remove(key);
            return Task.FromResult(expired.Count);
        }
    }

    public Task<IReadOnlyList<CharacterRecord>> ListCharactersAsync(long accountId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            IReadOnlyList<CharacterRecord> list = Live().Where(c => c.AccountId == accountId).OrderBy(c => c.Id).ToList();
            return Task.FromResult(list);
        }
    }

    public Task<(CreateCharacterOutcome Outcome, CharacterRecord? Character)> CreateCharacterAsync(CharacterRecord template, int maxPerAccount, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (Live().Any(c => string.Equals(c.Name, template.Name, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult<(CreateCharacterOutcome, CharacterRecord?)>((CreateCharacterOutcome.NameTaken, null));
            if (Live().Count(c => c.AccountId == template.AccountId) >= maxPerAccount)
                return Task.FromResult<(CreateCharacterOutcome, CharacterRecord?)>((CreateCharacterOutcome.TooManyCharacters, null));
            var character = template with { Id = _nextCharacterId++ };
            _characters.Add(character.Id, character);
            return Task.FromResult<(CreateCharacterOutcome, CharacterRecord?)>((CreateCharacterOutcome.Created, character));
        }
    }

    public Task<bool> DeleteCharacterAsync(long accountId, long characterId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            bool owned = _characters.TryGetValue(characterId, out var c) && c.AccountId == accountId && !_deleted.Contains(characterId);
            if (owned) _deleted.Add(characterId);
            return Task.FromResult(owned);
        }
    }

    public Task<CharacterRecord?> LoadCharacterAsync(long accountId, long characterId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(
                _characters.TryGetValue(characterId, out var c) && c.AccountId == accountId && !_deleted.Contains(characterId) ? c : null);
        }
    }

    public Task<CharacterData> LoadCharacterDataAsync(long characterId, CancellationToken ct = default)
    {
        lock (_lock) return Task.FromResult(_data.GetValueOrDefault(characterId) ?? CharacterData.Empty);
    }

    public Task SaveCharacterAsync(CharacterSnapshot s, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_characters.TryGetValue(s.CharacterId, out var c))
            {
                _characters[s.CharacterId] = c with
                {
                    MapId = s.MapId, X = s.X, Y = s.Y, Z = s.Z, Yaw = s.Yaw, Level = s.Level, Xp = s.Xp, Health = s.Health, Power = s.Power,
                    Money = s.Money, StarterKit = s.StarterKit,
                };
                _data[s.CharacterId] = new CharacterData(s.Items.ToArray(), s.Quests.Select(q => q with { Progress = q.Progress.ToArray() }).ToArray());
            }
        }
        return Task.CompletedTask;
    }

    private IEnumerable<CharacterRecord> Live() => _characters.Values.Where(c => !_deleted.Contains(c.Id));
}
