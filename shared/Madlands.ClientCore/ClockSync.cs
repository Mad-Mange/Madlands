using System;

namespace Madlands.ClientCore
{
    /// <summary>
    /// Estimates server time from Ping/Pong round trips (NTP-style). Uses the lowest-RTT sample in
    /// a sliding window (least affected by queuing delay) and slews toward it gradually so the
    /// estimate never jumps backward during play, which would make interpolation stutter.
    /// </summary>
    public sealed class ClockSync
    {
        private const int WindowSize = 8;
        private const double MaxSlewPerSampleMs = 10;
        private const double SnapThresholdMs = 500;

        private readonly double[] _offsets = new double[WindowSize];
        private readonly double[] _rtts = new double[WindowSize];
        private int _count;
        private int _next;
        private double _offset;

        public bool HasEstimate { get; private set; }
        public double OffsetMs => _offset;
        public double LastRttMs { get; private set; }
        public double BestRttMs { get; private set; }

        /// <summary>Initial rough estimate (e.g. from Welcome) before any round trip has completed.</summary>
        public void Seed(long serverTimeMs, long localTimeMs)
        {
            if (HasEstimate) return;
            _offset = serverTimeMs - localTimeMs;
            HasEstimate = true;
        }

        public void AddSample(long sentLocalMs, long receivedLocalMs, long serverTimeMs)
        {
            double rtt = Math.Max(0, receivedLocalMs - sentLocalMs);
            double offset = serverTimeMs + rtt / 2.0 - receivedLocalMs;
            LastRttMs = rtt;

            _offsets[_next] = offset;
            _rtts[_next] = rtt;
            _next = (_next + 1) % WindowSize;
            if (_count < WindowSize) _count++;

            int best = 0;
            for (int i = 1; i < _count; i++)
                if (_rtts[i] < _rtts[best]) best = i;
            BestRttMs = _rtts[best];
            double target = _offsets[best];

            if (!HasEstimate || Math.Abs(target - _offset) > SnapThresholdMs)
            {
                _offset = target;
                HasEstimate = true;
            }
            else
            {
                double delta = target - _offset;
                if (delta > MaxSlewPerSampleMs) delta = MaxSlewPerSampleMs;
                if (delta < -MaxSlewPerSampleMs) delta = -MaxSlewPerSampleMs;
                _offset += delta;
            }
        }

        public double ServerTimeAt(long localTimeMs) => localTimeMs + _offset;
    }
}
