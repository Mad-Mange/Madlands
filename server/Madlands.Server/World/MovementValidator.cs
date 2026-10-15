using Madlands.Shared;
using Madlands.Shared.Movement;
using Madlands.Shared.Terrain;

namespace Madlands.Server.World;

public enum MoveVerdict
{
    Accepted,
    /// <summary>Older than an already accepted update (reordering); silently ignored.</summary>
    Stale,
    /// <summary>Impossible move; the client gets a correction.</summary>
    Rejected,
}

/// <summary>
/// Server-side sanity checks for client-predicted movement (the client moves itself; the server
/// only accepts what is physically possible):
///  - client clock may not run faster than the server's (defeats "speed hacks" that accelerate time),
///  - horizontal distance per client-time step is bounded by run speed,
///  - position must be inside the map and near the ground (height map from Unity export).
/// </summary>
public sealed class MovementValidator
{
    public const long ClockToleranceMs = 750;
    public const long MaxClientLagMs = 2000;
    public const long MaxStepMs = 1000;
    public const float SpeedTolerance = 1.15f;
    public const float DistanceSlack = 0.3f;
    public const float MaxBelowGround = 1.0f;
    public const float MaxAboveGround = 10f;

    private readonly HeightMap _heightMap;
    private MovementState _last;
    private bool _hasClientTime;
    private long _lastClientMs;
    private long _anchorClientMs;
    private long _anchorServerMs;
    private long _resetServerMs;

    public MovementValidator(HeightMap heightMap)
    {
        _heightMap = heightMap;
    }

    public MovementState LastAccepted => _last;
    public string LastRejectReason { get; private set; } = "";

    /// <summary>Start over from a known-good state (spawn, teleport, correction).</summary>
    public void Reset(in MovementState state, long serverNowMs)
    {
        _last = state;
        _hasClientTime = false;
        _resetServerMs = serverNowMs;
    }

    public MoveVerdict Validate(in MovementState next, long clientMs, long serverNowMs)
    {
        if (!next.IsFinite) return Reject("non-finite values");
        if (_hasClientTime && clientMs <= _lastClientMs) return MoveVerdict.Stale;

        long stepMs;
        if (!_hasClientTime)
        {
            _anchorClientMs = clientMs;
            _anchorServerMs = serverNowMs;
            stepMs = serverNowMs - _resetServerMs;
        }
        else
        {
            long clientElapsed = clientMs - _anchorClientMs;
            long serverElapsed = serverNowMs - _anchorServerMs;
            if (clientElapsed > serverElapsed + ClockToleranceMs)
                return Reject($"client clock {clientElapsed - serverElapsed} ms ahead of server");
            if (clientElapsed < serverElapsed - MaxClientLagMs)
            {
                // Client fell far behind (hitch, packet burst). Re-anchor so the lag cannot be
                // banked and spent later as a burst of extra speed.
                _anchorClientMs = clientMs;
                _anchorServerMs = serverNowMs;
            }
            stepMs = clientMs - _lastClientMs;
        }

        float seconds = Math.Clamp(stepMs, 0, MaxStepMs) / 1000f;
        float maxDistance = GameConstants.RunSpeed * seconds * SpeedTolerance + DistanceSlack;
        float distance = Vec3.HorizontalDistance(_last.Position, next.Position);
        if (distance > maxDistance) return Reject($"moved {distance:0.00} m in {stepMs} ms (max {maxDistance:0.00} m)");
        if (next.Velocity.HorizontalLength > GameConstants.RunSpeed * 1.05f) return Reject("velocity above run speed");

        var p = next.Position;
        if (!_heightMap.Contains(p.X, p.Z)) return Reject("outside map bounds");
        float ground = _heightMap.GetHeight(p.X, p.Z);
        if (p.Y < ground - MaxBelowGround) return Reject($"{ground - p.Y:0.00} m below ground");
        if (p.Y > ground + MaxAboveGround) return Reject($"{p.Y - ground:0.00} m above ground");

        _last = next;
        _lastClientMs = clientMs;
        _hasClientTime = true;
        return MoveVerdict.Accepted;
    }

    private MoveVerdict Reject(string reason)
    {
        LastRejectReason = reason;
        return MoveVerdict.Rejected;
    }
}
