using System;

namespace Madlands.Shared
{
    public static class MathUtil
    {
        public const float Pi = MathF.PI;
        public const float TwoPi = MathF.PI * 2f;
        public const float Deg2Rad = MathF.PI / 180f;
        public const float Rad2Deg = 180f / MathF.PI;

        public static float Clamp(float value, float min, float max) =>
            value < min ? min : (value > max ? max : value);

        public static float Clamp01(float value) => Clamp(value, 0f, 1f);

        /// <summary>Wraps an angle in radians to the range (-PI, PI].</summary>
        public static float WrapAngle(float radians)
        {
            radians %= TwoPi;
            if (radians <= -Pi) radians += TwoPi;
            else if (radians > Pi) radians -= TwoPi;
            return radians;
        }

        /// <summary>Interpolates between two angles (radians) along the shortest arc.</summary>
        public static float LerpAngle(float from, float to, float t)
        {
            float delta = WrapAngle(to - from);
            return WrapAngle(from + delta * t);
        }

        /// <summary>Unit direction on the XZ plane for a yaw (0 = +Z, positive toward +X), like Unity.</summary>
        public static Vec3 YawToDirection(float yaw) => new Vec3(MathF.Sin(yaw), 0f, MathF.Cos(yaw));

        public static float DirectionToYaw(float x, float z) => MathF.Atan2(x, z);
    }
}
