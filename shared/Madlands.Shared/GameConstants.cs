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

        // Items, loot and quests.
        /// <summary>How close a player must be to talk to an NPC, trade with a vendor or loot a corpse.</summary>
        public const float InteractRange = 6f;
        public const int BackpackSlots = 16;
        /// <summary>Equipment slots that hold bags (Bag1-Bag4).</summary>
        public const int BagSlotCount = 4;
        /// <summary>The largest bag.</summary>
        public const int MaxBagSize = 36;
        public const int MaxQuestLog = 20;
        /// <summary>Money cap in copper (99,999 gold 99 silver 99 copper).</summary>
        public const long MaxMoney = 999_999_999;
    }
}
