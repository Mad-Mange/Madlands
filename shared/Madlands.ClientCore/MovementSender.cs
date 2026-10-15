using System;
using Madlands.Shared;
using Madlands.Shared.Movement;

namespace Madlands.ClientCore
{
    /// <summary>
    /// Decides when the local player's movement must be sent (WoW-style): immediately when the
    /// movement flags change, throttled when only the facing changes (mouse turning), on a
    /// heartbeat while moving, and whenever the real position drifts from what observers
    /// dead-reckon from the last sent state (e.g. after bumping into something).
    /// </summary>
    public sealed class MovementSender
    {
        public const long HeartbeatMs = 500;
        public const long MinIntervalMs = 100;
        public const float YawThreshold = 2f * MathUtil.Deg2Rad;
        public const float DriftThreshold = 0.5f;

        private MovementState _lastSent;
        private long _lastSentAt = long.MinValue;
        private bool _hasSent;

        public MovementState LastSent => _lastSent;

        /// <summary>Forget history, e.g. after a server correction, so the next update is sent at once.</summary>
        public void Reset()
        {
            _hasSent = false;
        }

        public bool ShouldSend(in MovementState current, long nowMs)
        {
            if (!_hasSent) return true;
            if (current.Flags != _lastSent.Flags) return true;

            long since = nowMs - _lastSentAt;
            if (since < MinIntervalMs) return false;

            if (Math.Abs(MathUtil.WrapAngle(current.Yaw - _lastSent.Yaw)) > YawThreshold) return true;

            bool moving = current.IsMoving || _lastSent.IsMoving;
            if (!moving) return false;

            if (since >= HeartbeatMs) return true;

            var predicted = _lastSent.Extrapolate(since / 1000f);
            if (Vec3.HorizontalDistanceSquared(predicted, current.Position) > DriftThreshold * DriftThreshold) return true;
            if (Math.Abs(current.Velocity.HorizontalLengthSquared - _lastSent.Velocity.HorizontalLengthSquared) > 0.01f) return true;
            return false;
        }

        public void MarkSent(in MovementState state, long nowMs)
        {
            _lastSent = state;
            _lastSentAt = nowMs;
            _hasSent = true;
        }
    }
}
