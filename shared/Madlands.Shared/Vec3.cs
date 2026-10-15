using System;
using System.Globalization;

namespace Madlands.Shared
{
    /// <summary>
    /// World-space vector. Coordinate convention is identical to Unity's so no conversion is ever
    /// needed: 1 unit = 1 meter, +Y is up, yaw 0 faces +Z, positive yaw turns toward +X.
    /// </summary>
    [Serializable]
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);

        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float LengthSquared => X * X + Y * Y + Z * Z;
        public float Length => MathF.Sqrt(LengthSquared);
        public float HorizontalLengthSquared => X * X + Z * Z;
        public float HorizontalLength => MathF.Sqrt(HorizontalLengthSquared);
        public Vec3 Horizontal => new Vec3(X, 0f, Z);
        public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);

        public Vec3 WithY(float y) => new Vec3(X, y, Z);

        public Vec3 Normalized
        {
            get
            {
                float len = Length;
                return len > 1e-6f ? this / len : Zero;
            }
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator *(float s, Vec3 a) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator /(Vec3 a, float s) => new Vec3(a.X / s, a.Y / s, a.Z / s);
        public static bool operator ==(Vec3 a, Vec3 b) => a.Equals(b);
        public static bool operator !=(Vec3 a, Vec3 b) => !a.Equals(b);

        public static float Distance(Vec3 a, Vec3 b) => (a - b).Length;
        public static float DistanceSquared(Vec3 a, Vec3 b) => (a - b).LengthSquared;
        public static float HorizontalDistance(Vec3 a, Vec3 b) => (a - b).HorizontalLength;
        public static float HorizontalDistanceSquared(Vec3 a, Vec3 b) => (a - b).HorizontalLengthSquared;

        public static Vec3 Lerp(Vec3 a, Vec3 b, float t) =>
            new Vec3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

        public bool Equals(Vec3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object obj) => obj is Vec3 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.00}, {1:0.00}, {2:0.00})", X, Y, Z);
    }
}
