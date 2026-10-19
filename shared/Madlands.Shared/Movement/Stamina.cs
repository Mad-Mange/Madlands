using System;

namespace Madlands.Shared.Movement
{
    /// <summary>
    /// Sprint stamina: sprinting (<see cref="MovementFlags.Sprinting"/>) makes you run faster and drains it,
    /// a short rest refills it. The client simulates it for its own movement and the stamina bar, the
    /// server with the same rules (on the client's movement timestamps) to decide whether the extra speed
    /// is allowed, so both arrive at the same numbers.
    /// </summary>
    public sealed class Stamina
    {
        public const float Max = 100f;
        /// <summary>About five and a half seconds of sprinting from full.</summary>
        public const float DrainPerSecond = 18f;
        /// <summary>About seven seconds to refill from empty, once the rest has begun.</summary>
        public const float RegenPerSecond = 14f;
        /// <summary>Stamina only comes back after this long without sprinting.</summary>
        public const long RegenDelayMs = 1500;
        /// <summary>A new sprint needs at least this much, so an empty bar does not stutter.</summary>
        public const float ToStart = 10f;
        public const float SpeedMultiplier = 1.4f;

        private long _lastSprintMs = long.MinValue / 2;

        public float Value { get; private set; } = Max;
        public bool Sprinting { get; private set; }
        public float Fraction => Value / Max;

        /// <summary>Whether a sprint may start (or go on) now.</summary>
        public bool CanSprint => Sprinting ? Value > 0f : Value >= ToStart;

        /// <summary>Advances over <paramref name="seconds"/> ending at <paramref name="nowMs"/>, sprinting during them or not.</summary>
        public void Advance(bool sprinting, float seconds, long nowMs)
        {
            if (seconds < 0f) seconds = 0f;
            if (sprinting)
            {
                Value = Math.Max(0f, Value - DrainPerSecond * seconds);
                _lastSprintMs = nowMs;
            }
            else if (nowMs - _lastSprintMs >= RegenDelayMs)
            {
                Value = Math.Min(Max, Value + RegenPerSecond * seconds);
            }
            Sprinting = sprinting;
        }

        /// <summary>Back to full (a new character, a resurrection).</summary>
        public void Refill()
        {
            Value = Max;
            Sprinting = false;
        }
    }
}
