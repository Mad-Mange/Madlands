using Madlands.Shared;
using Madlands.Shared.Movement;

namespace Madlands.ClientCore
{
    public struct RenderState
    {
        public Vec3 Position;
        public float Yaw;
        public MovementFlags Flags;
        /// <summary>True when the position was dead-reckoned past the newest sample.</summary>
        public bool Extrapolated;
    }

    /// <summary>
    /// Time-stamped movement samples of one remote entity. Rendering happens slightly in the past
    /// (<see cref="InterpolationDelayMs"/>): between two samples we interpolate, past the newest we
    /// dead-reckon with its velocity for up to <see cref="MaxExtrapolationMs"/>, then hold still.
    /// </summary>
    public sealed class InterpolationBuffer
    {
        public const double InterpolationDelayMs = 100;
        public const double MaxExtrapolationMs = 1000;
        private const int Capacity = 16;

        private readonly double[] _times = new double[Capacity];
        private readonly MovementState[] _states = new MovementState[Capacity];
        private int _start;
        private int _count;

        public int Count => _count;
        public double NewestTime => _count == 0 ? double.MinValue : _times[Index(_count - 1)];
        public MovementState Newest => _states[Index(_count - 1)];

        private int Index(int i) => (_start + i) % Capacity;

        /// <summary>Adds a sample; samples not newer than the newest one (duplicates, reordering) are ignored.</summary>
        public bool Add(double serverTimeMs, in MovementState state)
        {
            if (_count > 0 && serverTimeMs <= NewestTime) return false;
            if (_count == Capacity)
            {
                _start = (_start + 1) % Capacity;
                _count--;
            }
            int idx = Index(_count);
            _times[idx] = serverTimeMs;
            _states[idx] = state;
            _count++;
            return true;
        }

        public void Clear()
        {
            _start = 0;
            _count = 0;
        }

        public RenderState Sample(double renderTimeMs)
        {
            var result = new RenderState();
            if (_count == 0) return result;

            // Before the oldest sample: hold the oldest.
            if (renderTimeMs <= _times[Index(0)])
            {
                var first = _states[Index(0)];
                result.Position = first.Position;
                result.Yaw = first.Yaw;
                result.Flags = first.Flags;
                return result;
            }

            if (renderTimeMs < NewestTime)
            {
                for (int i = _count - 2; i >= 0; i--)
                {
                    double t0 = _times[Index(i)];
                    if (renderTimeMs < t0) continue;
                    double t1 = _times[Index(i + 1)];
                    var a = _states[Index(i)];
                    var b = _states[Index(i + 1)];
                    float t = (float)((renderTimeMs - t0) / (t1 - t0));
                    result.Position = Vec3.Lerp(a.Position, b.Position, t);
                    result.Yaw = MathUtil.LerpAngle(a.Yaw, b.Yaw, t);
                    result.Flags = t < 0.5f ? a.Flags : b.Flags;
                    return result;
                }
            }

            // At or past the newest sample: dead-reckon.
            var newest = Newest;
            double ahead = renderTimeMs - NewestTime;
            if (ahead > MaxExtrapolationMs) ahead = MaxExtrapolationMs;
            result.Position = newest.Extrapolate((float)(ahead / 1000.0));
            result.Yaw = newest.Yaw;
            result.Flags = newest.Flags;
            result.Extrapolated = true;
            return result;
        }
    }
}
