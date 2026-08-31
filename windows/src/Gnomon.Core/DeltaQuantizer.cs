namespace Gnomon.Core;

public sealed class DeltaQuantizer
{
    private readonly Dictionary<string, double> _seconds = new(StringComparer.OrdinalIgnoreCase);

    public void Accumulate(string category, TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero) return;
        _seconds[category] = _seconds.GetValueOrDefault(category) + elapsed.TotalSeconds;
    }

    public int FlushWholeMinutes(string category)
    {
        var seconds = _seconds.GetValueOrDefault(category);
        var minutes = (int)Math.Floor(seconds / 60d);
        _seconds[category] = seconds - minutes * 60d;
        return minutes;
    }

    public double RemainderSeconds(string category) => _seconds.GetValueOrDefault(category);
}
