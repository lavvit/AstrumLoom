namespace AstrumLoom;

// MultiBeat.FPS の AstrumLoom 版
public sealed class FpsCounter
{
    public double NowValue { get; private set; }

    private double _prevTimeMs;
    private readonly List<(double timeMs, double value)> _times = [];

    /// <summary>
    /// ゲーム内時間（秒）を渡して Tick する
    /// </summary>
    public void Tick(double totalSeconds)
    {
        double timeMs = totalSeconds * 1000.0;

        if (_prevTimeMs == 0)
        {
            _prevTimeMs = timeMs;
            return;
        }

        double etime = timeMs - _prevTimeMs;
        double fps = 1000.0 / Math.Max(0.001, etime);

        NowValue = Math.Round(fps, 3, MidpointRounding.AwayFromZero);
        _prevTimeMs = timeMs;

        _times.Add((timeMs, NowValue));

        // 直近 1 秒分だけ残す（or 件数1000までは許容）
        if (_times.Count > 1000 || _times[^1].timeMs - _times[0].timeMs > 1000.0)
        {
            _times.RemoveAt(0);
        }
    }

    public override string ToString()
        => $"{GetFPS(0.3):0.0} FPS ({GetMaxFPS(0.3):0}-{GetMinFPS(0.3):0})";

    public float Value => (float)NowValue;

    public double GetFPS(double rangeSeconds = 1.0)
    {
        try
        {
            var all = _times.ToArray();
            if (all.Length < 2) return 0;

            double border = _prevTimeMs - rangeSeconds * 1000.0;
            var target = all.Where(t => t.timeMs >= border).ToList();
            return target.Count < 2
                ? 0
                : Math.Round(target.Select(t => t.value).Average(), 3, MidpointRounding.AwayFromZero);
        }
        catch (ArgumentException)
        {
            return 0;
        }
    }

    public double GetMaxFPS(double rangeSeconds = 1.0)
    {
        var all = _times.ToArray();
        if (all.Length < 2) return 0;

        double border = _prevTimeMs - rangeSeconds * 1000.0;
        var target = all.Where(t => t.timeMs >= border).ToList();
        return target.Count < 2
            ? 0
            : Math.Round(target.Select(t => t.value).Max(), 3, MidpointRounding.AwayFromZero);
    }

    public double GetMinFPS(double rangeSeconds = 1.0)
    {
        var all = _times.ToArray();
        if (all.Length < 2) return 0;

        double border = _prevTimeMs - rangeSeconds * 1000.0;
        var target = all.Where(t => t.timeMs >= border).ToList();
        return target.Count < 2
            ? 0
            : Math.Round(target.Select(t => t.value).Min(), 3, MidpointRounding.AwayFromZero);
    }
}
