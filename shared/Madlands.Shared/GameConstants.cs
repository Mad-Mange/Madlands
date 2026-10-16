namespace Madlands.Shared
{
    /// <summary>
    /// Rules both sides must agree on. Changing any of these is a protocol change:
    /// bump <see cref="Net.ProtocolVersion.Current"/>.
    /// </summary>
    public static class GameConstants
    {
        public const int ServerTickRate = 20;
        public const int ServerTickMs = 1000 / ServerTickRate;

        // Movement (meters, seconds, radians). Values chosen to feel like classic WoW.
        public const float RunSpeed = 7.0f;
        public const float BackwardSpeed = 4.5f;
        public const float WalkSpeed = 2.5f;
        public const float TurnRate = MathUtil.Pi; // 180 degrees per second with keyboard turning
        public const float Gravity = 19.29f;
        public const float JumpVelocity = 7.96f;   // ~1.64 m jump height with the gravity above

        /// <summary>Maps must stay within +-MaxMapExtent of the origin to keep float precision (~1 mm).</summary>
        public const float MaxMapExtent = 8192f;

        // Combat (meters, milliseconds).
        /// <summary>Reach of melee attacks and melee abilities, center to center.</summary>
        public const float MeleeRange = 5f;
        /// <summary>The global cooldown most abilities trigger.</summary>
        public const int DefaultGcdMs = 1500;
        /// <summary>Eye height above the feet for line-of-sight checks.</summary>
        public const float EyeHeight = 1.5f;
        /// <summary>How far /say carries.</summary>
        public const float SayRange = 40f;
        /// <summary>Half-angle of the "in front of" cone for facing checks (radians, 90 degrees = front half).</summary>
        public const float FacingHalfAngle = MathUtil.Pi / 2f;
    }
}
