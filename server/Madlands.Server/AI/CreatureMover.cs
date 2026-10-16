using Madlands.Navigation;
using Madlands.Server.World;
using Madlands.Shared;
using Madlands.Shared.Movement;

namespace Madlands.Server.AI;

/// <summary>
/// Moves a creature along a navmesh path. The position is updated every tick (the server's truth),
/// but observers only get a new sample when the direction changes, and as a heartbeat while
/// moving; between samples their clients dead-reckon, exactly as for players.
/// </summary>
public sealed class CreatureMover
{
    public const long HeartbeatMs = 500;
    private const float ArriveDistance = 0.15f;

    private readonly Creature _creature;
    private readonly List<Vec3> _path = new();
    private int _next;
    private bool _walking;
    private long _lastSentMs;

    public CreatureMover(Creature creature)
    {
        _creature = creature;
    }

    public bool IsMoving => _next < _path.Count;
    /// <summary>Where the current path ends.</summary>
    public Vec3 Destination => _path.Count > 0 ? _path[^1] : _creature.Position;
    public PathResult LastPathResult { get; private set; } = PathResult.Complete;

    /// <summary>Starts moving toward <paramref name="destination"/> along the navmesh (straight line if there is none).</summary>
    public PathResult MoveTo(Vec3 destination, bool walk, long now)
    {
        var map = _creature.Map!;
        _path.Clear();
        var result = map.Nav.FindPath(_creature.Position, destination, _path);
        if (result == PathResult.None)
        {
            _path.Clear();
            _path.Add(_creature.Position);
            _path.Add(destination);
        }
        LastPathResult = result;
        // The first corner is the start position.
        _next = _path.Count > 1 ? 1 : _path.Count;
        _walking = walk;
        SkipReachedCorners();
        UpdateState(now, force: true);
        return result;
    }

    public void Stop(long now)
    {
        if (!IsMoving && !_creature.Movement.IsMoving) return;
        _path.Clear();
        _next = 0;
        UpdateState(now, force: true);
    }

    /// <summary>Turns in place toward a point (when standing still and fighting).</summary>
    public void Face(Vec3 point, long now)
    {
        var to = (point - _creature.Position).Horizontal;
        if (to.HorizontalLengthSquared < 0.01f) return;
        float yaw = MathUtil.DirectionToYaw(to.X, to.Z);
        if (MathF.Abs(MathUtil.WrapAngle(yaw - _creature.Movement.Yaw)) < 5f * MathUtil.Deg2Rad) return;
        _creature.Movement.Yaw = yaw;
        _creature.MovementTimeMs = now;
        _creature.LastChangeMs = now;
    }

    public void Update(long now, float dt)
    {
        if (!IsMoving) return;
        if (_creature.IsImmobile)
        {
            Stop(now);
            return;
        }

        float speed = (_walking ? _creature.Template.WalkSpeed : _creature.Template.RunSpeed) * _creature.SpeedMultiplier;
        float remaining = speed * dt;
        var position = _creature.Position;
        int segmentBefore = _next;
        while (remaining > 0f && _next < _path.Count)
        {
            var target = _path[_next];
            float distance = Vec3.HorizontalDistance(position, target);
            if (distance <= remaining)
            {
                position = new Vec3(target.X, position.Y, target.Z);
                remaining -= distance;
                _next++;
            }
            else
            {
                var dir = (target - position).Horizontal.Normalized;
                position += dir * remaining;
                remaining = 0f;
            }
        }

        var map = _creature.Map!;
        _creature.Movement.Position = position.WithY(map.GroundHeight(position.X, position.Z));
        map.OnMoved(_creature);
        bool turned = _next != segmentBefore;
        UpdateState(now, force: turned || !IsMoving);
    }

    private void SkipReachedCorners()
    {
        while (_next < _path.Count && Vec3.HorizontalDistance(_creature.Position, _path[_next]) < ArriveDistance) _next++;
    }

    /// <summary>Sets velocity, facing and flags from the path; broadcasts a new sample on changes and heartbeats.</summary>
    private void UpdateState(long now, bool force)
    {
        ref var m = ref _creature.Movement;
        if (IsMoving)
        {
            var dir = (_path[_next] - _creature.Position).Horizontal.Normalized;
            float speed = (_walking ? _creature.Template.WalkSpeed : _creature.Template.RunSpeed) * _creature.SpeedMultiplier;
            m.Velocity = dir * speed;
            if (dir.HorizontalLengthSquared > 0f) m.Yaw = MathUtil.DirectionToYaw(dir.X, dir.Z);
            m.Flags = MovementFlags.Forward | (_walking ? MovementFlags.Walking : MovementFlags.None);
        }
        else
        {
            m.Velocity = Vec3.Zero;
            m.Flags = MovementFlags.None;
        }
        _creature.MovementTimeMs = now;
        if (force || now - _lastSentMs >= HeartbeatMs)
        {
            _creature.LastChangeMs = now;
            _lastSentMs = now;
        }
    }
}
