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
        /// <summary>Running faster on <see cref="Stamina"/>. Only in the client's own updates: others see the speed.</summary>
        Sprinting = 1 << 8,

        MoveMask = Forward | Backward | StrafeLeft | StrafeRight,
        TurnMask = TurnLeft | TurnRight,
        /// <summary>The flags other clients get (one byte in the compact form); higher ones stay between client and server.</summary>
        CompactMask = 0xFF,
    }

    /// <summary>
    /// Snapshot of an entity's movement, sent WoW-style on changes plus periodic heartbeats.
    /// Position is at the feet. Velocity: horizontal = intended move velocity; vertical = only
    /// while <see cref="MovementFlags.Airborne"/> (grounded entities follow the terrain).
    /// </summary>
    public struct MovementState : IEquatable<MovementState>
    {
        public const int WireSize = 12 + 4 + 12 + 4;
        /// <summary>Size of <see cref="WriteCompact"/>: 3 x 24-bit position, 16-bit yaw, 3 x 16-bit velocity, 8-bit flags.</summary>
        public const int CompactWireSize = 9 + 2 + 6 + 1;
        /// <summary>Compact positions are in 1/512 m (about 2 mm); 24 bits cover +-16384 m, twice the map limit.</summary>
        public const float PositionScale = 512f;
        /// <summary>Compact velocities are in 1/256 m/s, up to +-128 m/s.</summary>
        public const float VelocityScale = 256f;
        private const float YawScale = 65536f / (2f * MathUtil.Pi);

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

        /// <summary>
        /// The compact form used to broadcast other entities' movement (18 bytes instead of 32): precise
        /// enough to draw them, while the full-precision form stays for a client's own movement.
        /// All movement flags fit in the byte.
        /// </summary>
        public void WriteCompact(PacketWriter w)
        {
            w.WriteInt24(Quantize(Position.X, PositionScale));
            w.WriteInt24(Quantize(Position.Y, PositionScale));
            w.WriteInt24(Quantize(Position.Z, PositionScale));
            w.WriteUShort((ushort)(Quantize(MathUtil.WrapAngle(Yaw), YawScale) & 0xFFFF));
            w.WriteShort(QuantizeShort(Velocity.X));
            w.WriteShort(QuantizeShort(Velocity.Y));
            w.WriteShort(QuantizeShort(Velocity.Z));
            w.WriteByte((byte)(Flags & MovementFlags.CompactMask));
        }

        public static MovementState ReadCompact(ref PacketReader r)
        {
            MovementState s;
            float x = r.ReadInt24() / PositionScale;
            float y = r.ReadInt24() / PositionScale;
            float z = r.ReadInt24() / PositionScale;
            s.Position = new Vec3(x, y, z);
            s.Yaw = MathUtil.WrapAngle(r.ReadUShort() / YawScale);
            float vx = r.ReadShort() / VelocityScale;
            float vy = r.ReadShort() / VelocityScale;
            float vz = r.ReadShort() / VelocityScale;
            s.Velocity = new Vec3(vx, vy, vz);
            s.Flags = (MovementFlags)r.ReadByte();
            return s;
        }

        /// <summary>This state as it arrives after a <see cref="WriteCompact"/> roundtrip.</summary>
        public MovementState Quantized()
        {
            var w = new PacketWriter(CompactWireSize);
            WriteCompact(w);
            var r = new PacketReader(w.AsSpan());
            return ReadCompact(ref r);
        }

        private static int Quantize(float value, float scale) => (int)Math.Round(value * scale);

        private static short QuantizeShort(float value) => (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Quantize(value, VelocityScale)));

        public bool Equals(MovementState other) =>
            Position.Equals(other.Position) && Yaw.Equals(other.Yaw) &&
            Velocity.Equals(other.Velocity) && Flags == other.Flags;

        public override bool Equals(object obj) => obj is MovementState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Position, Yaw, Velocity, Flags);
        public override string ToString() => "pos=" + Position + " yaw=" + Yaw.ToString("0.00") + " vel=" + Velocity + " flags=" + Flags;
    }
}
