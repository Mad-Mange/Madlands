namespace Madlands.Server.Diagnostics;

/// <summary>Collects tick durations between reports. Only touched from the loop thread.</summary>
public sealed class TickStats
{
    private readonly List<double> _samples = new(1024);

    public readonly record struct Snapshot(int Ticks, double AverageMs, double P99Ms, double MaxMs);

    public void RecordTick(double milliseconds) => _samples.Add(milliseconds);

    public Snapshot TakeSnapshot()
    {
        if (_samples.Count == 0) return default;
        _samples.Sort();
        var snapshot = new Snapshot(
            _samples.Count,
            _samples.Average(),
            _samples[Math.Min(_samples.Count - 1, (int)(_samples.Count * 0.99))],
            _samples[^1]);
        _samples.Clear();
        return snapshot;
    }
}
