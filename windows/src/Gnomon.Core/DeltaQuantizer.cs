namespace Gnomon.Core;

public sealed class DeltaQuantizer
{
    private readonly Dictionary<string, double> _seconds = new(StringComparer.OrdinalIgnoreCase);

    public void Accumulate(string category, TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero) return;
        _seconds.TryGetValue(category, out var current);
        _seconds[category] = current + elapsed.TotalSeconds;
    }

    public int FlushWholeMinutes(string category)
    {
        _seconds.TryGetValue(category, out var seconds);
        var minutes = (int)Math.Floor(seconds / 60d);
        _seconds[category] = seconds - minutes * 60d;
        return minutes;
    }

    public double RemainderSeconds(string category) =>
        _seconds.TryGetValue(category, out var seconds) ? seconds : 0;
}
