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
        if (etime < 0) return;
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

public class FPS
{
    private static (float t, float draw, float update) _fpsHistory = (0, 0, 0);
    public static string GetFPSString() => $"FPS: {AstrumCore.NowFPS} / U: {AstrumCore.UpdateFPS.GetFPS():0.0}";
    public static void Draw(ReferencePoint point = ReferencePoint.TopLeft)
    {
        var platform = AstrumCore.Platform;
        float reloadFrame = 60f;
        if (platform.Time.TotalTime - _fpsHistory.t >= 1f / reloadFrame)
        {
            _fpsHistory = (platform.Time.TotalTime,
                (float)AstrumCore.NowFPSValue.draw,
                (float)AstrumCore.NowFPSValue.update);
        }

        int size = 10;
        int x = 10, y = 10;
        switch (point)
        {
            case ReferencePoint.TopRight:
                x = AstrumCore.Width - 20 - (Length + (AstrumCore.MultiThreading ? 2 : 0)) * size;
                y = 10;
                break;
            case ReferencePoint.BottomLeft:
                x = 10;
                y = AstrumCore.Height - 10 - size * (AstrumCore.MultiThreading ? 2 : 1);
                break;
            case ReferencePoint.BottomRight:
                x = AstrumCore.Width - 20 - (Length + 2) * size;
                y = AstrumCore.Height - 10 - size * (AstrumCore.MultiThreading ? 2 : 1);
                break;
        }
        var color = Sleep.Sleeping ? Color.Violet : AstrumCore.VSync ? Color.Cyan : Color.Lime;
        ShapeText.Draw(x, y, AstrumCore.MultiThreading ? $"D:{_fpsHistory.draw:0}\nU:{_fpsHistory.update:0}" : $"{_fpsHistory.draw:0}",
            size: size, color: color, thickness: 2);
    }

    private static int Length => Math.Max((int)_fpsHistory.draw, (int)_fpsHistory.update).ToString().Length;
}