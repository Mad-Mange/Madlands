using Madlands.ClientCore;
using Madlands.Shared;
using Madlands.Shared.Movement;
using Madlands.Shared.Terrain;

namespace Madlands.Bots;

/// <summary>
/// A headless player that wanders around the spawn area: runs to random points, sometimes
/// pauses or jumps. Moves with the same rules and the same <see cref="MovementSender"/> as the
/// real client, so the server cannot tell it apart from a human.
/// </summary>
public sealed class Bot
{
    private readonly GameClient _client;
    private readonly MovementSender _sender = new();
    private readonly HeightMap _heightMap;
    private readonly Random _random;
    private readonly float _wanderRadius;

    private MovementState _state;
    private Vec3 _center;
    private Vec3 _target;
    private long _pauseUntilMs;
    private bool _inWorld;

    public Bot(string name, HeightMap heightMap, float wanderRadius, int seed)
    {
        Name = name;
        _heightMap = heightMap;
        _wanderRadius = wanderRadius;
        _random = new Random(seed);
        _client = new GameClient(manualMode: true);
        _client.EnteredWorld += OnEnteredWorld;
        _client.Corrected += OnCorrected;
        _client.Disconnected += reason => { _inWorld = false; LastError = reason; };
    }

    public string Name { get; }
    public GameClient Client => _client;
    public int Corrections { get; private set; }
    public string? LastError { get; private set; }

    public void Connect(string host, int port) => _client.Connect(host, port, Name);

    public void Update(float dt)
    {
        _client.Update();
        if (!_inWorld || _client.State != ClientState.InWorld) return;

        long now = _client.LocalTimeMs;
        Simulate(dt, now);
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
        PickTarget();
    }

    private void OnCorrected(MovementState state)
    {
        _state = state;
        _sender.Reset();
        Corrections++;
        PickTarget();
    }

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

        if ((flags & MovementFlags.Airborne) == 0)
        {
            if (now < _pauseUntilMs)
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
            vel = new Vec3(dir.X * GameConstants.RunSpeed, 0f, dir.Z * GameConstants.RunSpeed);
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
