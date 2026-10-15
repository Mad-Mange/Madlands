using System;
using Madlands.Shared.Net;

namespace Madlands.Shared.Movement
{
    [Flags]
    public enum MovementFlags : uint
    {
        None = 0,
        Forward = 1 << 0,
        Backward = 1 << 1,
        StrafeLeft = 1 << 2,
        StrafeRight = 1 << 3,
        TurnLeft = 1 << 4,
        TurnRight = 1 << 5,
        /// <summary>Jumping or falling: vertical motion follows gravity, not the terrain.</summary>
        Airborne = 1 << 6,
        Walking = 1 << 7,

        MoveMask = Forward | Backward | StrafeLeft | StrafeRight,
        TurnMask = TurnLeft | TurnRight,
    }

    /// <summary>
    /// Snapshot of an entity's movement, sent WoW-style on changes plus periodic heartbeats.
    /// Position is at the feet. Velocity: horizontal = intended move velocity; vertical = only
    /// while <see cref="MovementFlags.Airborne"/> (grounded entities follow the terrain).
    /// </summary>
    public struct MovementState : IEquatable<MovementState>
    {
        public const int WireSize = 12 + 4 + 12 + 4;

        public Vec3 Position;
        public float Yaw;
        public Vec3 Velocity;
        public MovementFlags Flags;

        public MovementState(Vec3 position, float yaw, Vec3 velocity, MovementFlags flags)
        {
            Position = position;
            Yaw = yaw;
            Velocity = velocity;
            Flags = flags;
        }

        public static MovementState Idle(Vec3 position, float yaw) =>
            new MovementState(position, yaw, Vec3.Zero, MovementFlags.None);

        public bool IsAirborne => (Flags & MovementFlags.Airborne) != 0;
        public bool IsMoving => Velocity.HorizontalLengthSquared > 1e-6f || IsAirborne;

        public bool IsFinite => Position.IsFinite && Velocity.IsFinite && float.IsFinite(Yaw);

        /// <summary>
        /// Dead-reckons this state forward in time. Grounded entities keep their Y (callers that
        /// know the terrain snap it); airborne entities follow a ballistic arc.
        /// </summary>
        public Vec3 Extrapolate(float seconds)
        {
            float x = Position.X + Velocity.X * seconds;
            float z = Position.Z + Velocity.Z * seconds;
            float y = Position.Y;
            if (IsAirborne)
                y += Velocity.Y * seconds - 0.5f * GameConstants.Gravity * seconds * seconds;
            return new Vec3(x, y, z);
        }

        public void Write(PacketWriter w)
        {
            w.WriteVec3(Position);
            w.WriteFloat(Yaw);
            w.WriteVec3(Velocity);
            w.WriteUInt((uint)Flags);
        }

        public static MovementState Read(ref PacketReader r)
        {
            MovementState s;
            s.Position = r.ReadVec3();
            s.Yaw = r.ReadFloat();
            s.Velocity = r.ReadVec3();
            s.Flags = (MovementFlags)r.ReadUInt();
            return s;
        }

        public bool Equals(MovementState other) =>
            Position.Equals(other.Position) && Yaw.Equals(other.Yaw) &&
            Velocity.Equals(other.Velocity) && Flags == other.Flags;

        public override bool Equals(object obj) => obj is MovementState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Position, Yaw, Velocity, Flags);
        public override string ToString() => "pos=" + Position + " yaw=" + Yaw.ToString("0.00") + " vel=" + Velocity + " flags=" + Flags;
    }
}
