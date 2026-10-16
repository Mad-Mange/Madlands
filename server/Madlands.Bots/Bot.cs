using Madlands.ClientCore;
using Madlands.Shared;
using Madlands.Shared.Game;
using Madlands.Shared.Movement;
using Madlands.Shared.Terrain;

namespace Madlands.Bots;

/// <summary>
/// A headless player. In "wander" mode it runs to random points near the spawn, sometimes pausing
/// or jumping; in "fight" mode it also hunts nearby creatures: runs into range, faces them,
/// auto-attacks and uses its class's spells, heals itself when low, and releases when it dies.
/// Moves with the same rules and the same <see cref="MovementSender"/> as the real client, so the
/// server cannot tell it apart from a human.
/// </summary>
public sealed class Bot
{
    private const float HuntRadius = 60f;
    private const float MeleeEngage = 3.5f;
    private const float CasterEngage = 24f;
    private const long CastAttemptIntervalMs = 300;

    private readonly GameClient _client;
    private readonly MovementSender _sender = new();
    private readonly HeightMap _heightMap;
    private readonly Random _random;
    private readonly float _wanderRadius;
    private readonly bool _fight;
    private readonly int _index;

    private MovementState _state;
    private Vec3 _center;
    private Vec3 _target;
    private long _pauseUntilMs;
    private bool _inWorld;
    private Task<(string Host, int Port, string Token, ulong CharacterId)>? _login;
    private EntityGuid _enemy;
    private long _nextCastAttemptMs;
    private long _diedAtMs = -1;

    public Bot(int index, HeightMap heightMap, float wanderRadius, bool fight = false)
    {
        _index = index;
        Name = "Bot" + Letters(index);
        Username = $"bot{index + 1:000}";
        _heightMap = heightMap;
        _wanderRadius = wanderRadius;
        _fight = fight;
        _random = new Random(1000 + index);
        _client = new GameClient(manualMode: true);
        _client.EnteredWorld += OnEnteredWorld;
        _client.Corrected += OnCorrected;
        _client.Disconnected += reason => { _inWorld = false; LastError = reason; };
        _client.CombatEventReceived += OnCombatEvent;
        _client.ExperienceReceived += (amount, _) => XpGained += amount;
    }

    /// <summary>Character name; letters only (name rules), "Bot" prefix so clients can tint bots.</summary>
    public string Name { get; }
    public string Username { get; }
    public GameClient Client => _client;
    public int Corrections { get; private set; }
    public string? LastError { get; private set; }
    public bool LoggingIn => _login is not null;
    public int Kills { get; private set; }
    public int Deaths { get; private set; }
    public int XpGained { get; private set; }
    public int DamageDone { get; private set; }

    /// <summary>Logs in (registering the bot's account and character on first run), then enters the world.</summary>
    public void Start(AccountClient account) => _login = LoginAsync(account);

    private async Task<(string, int, string, ulong)> LoginAsync(AccountClient account)
    {
        var auth = await account.LoginOrRegisterAsync(Username, $"bot-password-{_index + 1:000}");
        if (!auth.Ok) throw new InvalidOperationException($"login: {auth.Result} {auth.Message}");

        var list = await account.GetCharactersAsync();
        if (!list.Ok) throw new InvalidOperationException($"character list: {list.Result} {list.Message}");

        ulong characterId = list.Characters.FirstOrDefault(c => c.Name == Name).Id;
        if (characterId == 0)
        {
            var race = list.Races[_index % list.Races.Count].Id;
            var cls = list.Classes[_index % list.Classes.Count].Id;
            var created = await account.CreateCharacterAsync(Name, race, cls);
            if (!created.Ok) throw new InvalidOperationException($"create character: {created.Result} {created.Message}");
            characterId = created.Character.Id;
        }
        return (auth.RealmHost, auth.RealmPort, auth.SessionToken, characterId);
    }

    private static string Letters(int i) =>
        new string(new[] { (char)('a' + i / 676 % 26), (char)('a' + i / 26 % 26), (char)('a' + i % 26) });

    public void Update(float dt)
    {
        if (_login is { IsCompleted: true } login)
        {
            _login = null;
            if (login.IsFaulted) LastError = login.Exception!.InnerException!.Message;
            else _client.Connect(login.Result.Host, login.Result.Port, login.Result.Token, login.Result.CharacterId);
        }

        _client.Update();
        if (!_inWorld || _client.State != ClientState.InWorld) return;

        long now = _client.LocalTimeMs;
        if (_fight && HandleDeath(now)) return;
        if (_fight) Fight(dt, now);
        else Simulate(dt, now);
        _client.SetLocalMovement(_state);
        if (_sender.ShouldSend(_state, now))
        {
            _client.SendMovement(_state);
            _sender.MarkSent(_state, now);
        }
    }

    public void Disconnect() => _client.Disconnect();

    private void OnEnteredWorld(MovementState spawn)
    {
        _state = spawn;
        _center = spawn.Position;
        _inWorld = true;
        _sender.Reset();
        _enemy = EntityGuid.Empty;
        PickTarget();
    }

    private void OnCorrected(MovementState state)
    {
        _state = state;
        _sender.Reset();
        if (!_client.Self.Values.IsDead) Corrections++;
        PickTarget();
    }

    private void OnCombatEvent(Madlands.Shared.Net.CombatEvent e)
    {
        if (e.Source == _client.LocalGuid && e.Amount > 0 && !e.Has(CombatFlags.Heal)) DamageDone += e.Amount;
        if (e.Source == _client.LocalGuid && e.Has(CombatFlags.KillingBlow)) Kills++;
        // Something hit us while we were not fighting: fight back.
        if (_fight && e.Target == _client.LocalGuid && _enemy.IsEmpty && e.Source.Type == EntityType.Creature) _enemy = e.Source;
    }

    // ------------------------------------------------------------------ fighting

    /// <returns>True while dead (nothing else to do).</returns>
    private bool HandleDeath(long now)
    {
        if (!_client.Self.Values.IsDead)
        {
            _diedAtMs = -1;
            return false;
        }
        if (_diedAtMs < 0)
        {
            _diedAtMs = now;
            Deaths++;
            _enemy = EntityGuid.Empty;
        }
        if (now - _diedAtMs > 3000)
        {
            _client.ReleaseSpirit();
            _diedAtMs = now + 5000; // do not spam while the release is on its way
        }
        _state.Velocity = Vec3.Zero;
        _state.Flags = MovementFlags.None;
        return true;
    }

    private void Fight(float dt, long now)
    {
        var self = _client.Self.Values;
        if (!IsValidEnemy(_enemy)) _enemy = FindEnemy();
        if (_enemy.IsEmpty || !_client.TryGetPosition(_enemy, out var enemyPos))
        {
            if (!_client.Target.IsEmpty) _client.SetTarget(EntityGuid.Empty);
            Simulate(dt, now);
            return;
        }
        if (_client.Target != _enemy) _client.SetTarget(_enemy);

        bool caster = self.PowerType == PowerType.Mana;
        float engage = caster ? CasterEngage : MeleeEngage;
        var to = (enemyPos - _state.Position).Horizontal;
        float distance = to.HorizontalLength;
        _state.Yaw = MathUtil.DirectionToYaw(to.X, to.Z);

        bool casting = _client.Self.Cast != null;
        bool canMove = !self.Has(UnitFlags.Rooted) && !self.Has(UnitFlags.Stunned);
        if (distance > engage && !casting && canMove)
        {
            float speed = GameConstants.RunSpeed * self.SpeedPercent / 100f;
            var dir = to.Normalized;
            MoveGrounded(dir * speed, dt);
            return;
        }

        StandStill();
        if (casting || now < _nextCastAttemptMs) return;
        _nextCastAttemptMs = now + CastAttemptIntervalMs;

        if (!self.Has(UnitFlags.AutoAttacking) && distance <= GameConstants.MeleeRange) _client.CastSpell(AttackSpell(), _enemy);
        // Heal up first (wardens), then the strongest usable ability.
        if (self.HealthFraction < 0.5f && TryCast(SpellTarget.Friend, _client.LocalGuid)) return;
        TryCast(SpellTarget.Enemy, _enemy);
        TryCast(SpellTarget.None, _client.LocalGuid);
    }

    private bool TryCast(SpellTarget kind, EntityGuid target)
    {
        foreach (var id in _client.KnownSpells.OrderByDescending(id => id))
        {
            if (!_client.Data.TryGetSpell(id, out var spell) || spell.IsAutoAttack || spell.Target != kind) continue;
            if (_client.CheckCast(id, target) != CastResult.Success) continue;
            _client.CastSpell(id, target);
            return true;
        }
        return false;
    }

    private uint AttackSpell()
    {
        foreach (var id in _client.KnownSpells)
            if (_client.Data.TryGetSpell(id, out var s) && s.IsAutoAttack) return id;
        return 1;
    }

    private bool IsValidEnemy(EntityGuid guid) =>
        !guid.IsEmpty && _client.CanAttack(guid) && _client.TryGetUnit(guid, out var u) && !u.Values.Has(UnitFlags.Evading);

    /// <summary>The nearest creature we may attack (not training dummies: they never die).</summary>
    private EntityGuid FindEnemy()
    {
        EntityGuid best = EntityGuid.Empty;
        float bestDistance = HuntRadius;
        foreach (var e in _client.World.Entities)
        {
            if (e.Guid.Type != EntityType.Creature || e.Name == "Training Dummy" || !IsValidEnemy(e.Guid)) continue;
            float d = Vec3.HorizontalDistance(e.Movement.Sample(_client.RenderTimeMs).Position, _state.Position);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = e.Guid;
            }
        }
        return best;
    }

    private void StandStill()
    {
        _state.Velocity = Vec3.Zero;
        _state.Flags &= MovementFlags.Airborne;
        if ((_state.Flags & MovementFlags.Airborne) == 0) _state.Position = _state.Position.WithY(_heightMap.GetHeight(_state.Position.X, _state.Position.Z));
    }

    private void MoveGrounded(Vec3 velocity, float dt)
    {
        float nx = Math.Clamp(_state.Position.X + velocity.X * dt, _heightMap.OriginX + 1, _heightMap.MaxX - 1);
        float nz = Math.Clamp(_state.Position.Z + velocity.Z * dt, _heightMap.OriginZ + 1, _heightMap.MaxZ - 1);
        _state.Position = new Vec3(nx, _heightMap.GetHeight(nx, nz), nz);
        _state.Velocity = velocity.WithY(0f);
        _state.Flags = MovementFlags.Forward;
    }

    // ------------------------------------------------------------------ wandering

    private void PickTarget()
    {
        float angle = (float)(_random.NextDouble() * MathUtil.TwoPi);
        float distance = (float)Math.Sqrt(_random.NextDouble()) * _wanderRadius;
        float x = Math.Clamp(_center.X + MathF.Sin(angle) * distance, _heightMap.OriginX + 2, _heightMap.MaxX - 2);
        float z = Math.Clamp(_center.Z + MathF.Cos(angle) * distance, _heightMap.OriginZ + 2, _heightMap.MaxZ - 2);
        _target = new Vec3(x, 0, z);
    }

    private void Simulate(float dt, long now)
    {
        var pos = _state.Position;
        var vel = _state.Velocity;
        var flags = _state.Flags;
        var self = _client.Self.Values;
        float speed = GameConstants.RunSpeed * self.SpeedPercent / 100f;
        bool canMove = !self.Has(UnitFlags.Rooted) && !self.Has(UnitFlags.Stunned) && !self.IsDead;

        if ((flags & MovementFlags.Airborne) == 0)
        {
            if (now < _pauseUntilMs || !canMove)
            {
                _state.Velocity = Vec3.Zero;
                _state.Flags = MovementFlags.None;
                return;
            }

            var toTarget = (_target - pos).Horizontal;
            if (toTarget.HorizontalLength < 2f)
            {
                if (_random.NextDouble() < 0.4) _pauseUntilMs = now + _random.Next(1000, 4000);
                PickTarget();
                return;
            }

            // Turn toward the target like a player steering with the mouse.
            float desired = MathUtil.DirectionToYaw(toTarget.X, toTarget.Z);
            float diff = MathUtil.WrapAngle(desired - _state.Yaw);
            float maxTurn = GameConstants.TurnRate * 1.5f * dt;
            _state.Yaw = MathUtil.WrapAngle(_state.Yaw + Math.Clamp(diff, -maxTurn, maxTurn));

            var dir = MathUtil.YawToDirection(_state.Yaw);
            vel = new Vec3(dir.X * speed, 0f, dir.Z * speed);
            flags = MovementFlags.Forward;

            if (_random.NextDouble() < 0.15 * dt) // roughly one jump every ~7 seconds
            {
                flags |= MovementFlags.Airborne;
                vel = vel.WithY(GameConstants.JumpVelocity);
            }
        }

        float nx = pos.X + vel.X * dt;
        float nz = pos.Z + vel.Z * dt;
        float ground = _heightMap.GetHeight(nx, nz);
        float ny;
        if ((flags & MovementFlags.Airborne) != 0)
        {
            float vy = vel.Y - GameConstants.Gravity * dt;
            ny = pos.Y + vel.Y * dt - 0.5f * GameConstants.Gravity * dt * dt;
            if (ny <= ground)
            {
                ny = ground;
                vy = 0f;
                flags &= ~MovementFlags.Airborne;
            }
            vel = vel.WithY(vy);
        }
        else
        {
            ny = ground;
        }

        _state.Position = new Vec3(nx, ny, nz);
        _state.Velocity = (flags & MovementFlags.Airborne) != 0 ? vel : vel.WithY(0f);
        _state.Flags = flags;
    }
}
