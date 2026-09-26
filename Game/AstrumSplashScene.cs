using System.Diagnostics;

namespace AstrumLoom;

/// <summary>GameApp の起動ロゴ。時計で進めるので描画・更新レートやバックエンドに依存しない。</summary>
internal sealed class AstrumSplashScene(Scene next) : Scene
{
    private readonly Stopwatch _clock = new();
    private Texture? _image;
    private bool _showOverlay;
    private bool _showLog;
    private const double Duration = 2.5;

    public override void Enable()
    {
        using var image = typeof(AstrumSplashScene).Assembly.GetManifestResourceStream("AstrumLoom.astrumloom-splash.png")
            ?? throw new InvalidOperationException("埋め込みスプラッシュ画像が見つかりません。");
        using var buffer = new MemoryStream();
        image.CopyTo(buffer);
        _image = new Texture(buffer.ToArray(), ".png");
        _showOverlay = DebugControl.ShowOverlay;
        _showLog = Log.DrawOnScreen;
        DebugControl.ShowOverlay = false;
        Log.DrawOnScreen = false;
        _clock.Start();
    }

    public override void Update()
    {
        if (_clock.Elapsed.TotalSeconds < Duration && !Key.Enter.Push() && !Key.Space.Push() && !Key.Esc.Push())
            return;

        Scene.Change(next);
    }

    public override void Draw()
    {
        double time = _clock.Elapsed.TotalSeconds;
        double sx = AstrumCore.Width / 1600.0;
        double sy = AstrumCore.Height / 900.0;
        Drawing.Fill(new Color(13, 24, 43));

        // 両側から 2 本の光の糸をたぐり、中央の星を点灯する。
        double reveal = Smooth(Math.Clamp(time / 0.95, 0, 1));
        DrawThread(reveal, sx, sy, true);
        DrawThread(reveal, sx, sy, false);
        double light = Smooth(Math.Clamp((time - 0.7) / 0.4, 0, 1));
        if (light > 0)
        {
            Drawing.Circle(800 * sx, 351 * sy, 11 * Math.Min(sx, sy) * light,
                new Color(230, 255, 250), opacity: light);
            Drawing.Line(800 * sx, (351 - 28 * light) * sy, 0, 56 * light * sy,
                new Color(229, 255, 252), 2, opacity: light);
            Drawing.Line((800 - 28 * light) * sx, 351 * sy, 56 * light * sx, 0,
                new Color(229, 255, 252), 2, opacity: light);
        }

        // 完成したロゴへ溶かし込み、少し見せてから暗転してメニューへ。
        double imageAlpha = Smooth(Math.Clamp((time - 0.85) / 0.55, 0, 1));
        if (_image?.IsReady == true && imageAlpha > 0)
            _image.Draw(0, 0, s: (sx, sy), opacity: imageAlpha);
        double fade = Smooth(Math.Clamp((time - 2.1) / 0.4, 0, 1));
        if (fade > 0)
            Drawing.Box(0, 0, AstrumCore.Width, AstrumCore.Height, new Color(10, 10, 11), opacity: fade);
    }

    public override void Disable()
    {
        _clock.Stop();
        DebugControl.ShowOverlay = _showOverlay;
        Log.DrawOnScreen = _showLog;
        _image?.Dispose();
        _image = null;
        base.Disable();
    }

    private static double Smooth(double x) => x * x * (3 - 2 * x);

    private static void DrawThread(double progress, double sx, double sy, bool upper)
    {
        // SVG と同じ 2 本の三次ベジェ。左半分から右半分へ順に線を伸ばす。
        double y0 = upper ? 412 : 290;
        double yMid = upper ? 251 : 450;
        double yEnd = y0;
        var color = upper ? new Color(139, 221, 235) : new Color(181, 166, 242);
        const int Segments = 72;
        int count = (int)Math.Ceiling(progress * Segments);
        for (int i = 0; i < count; i++)
        {
            double t0 = 2.0 * i / Segments;
            double t1 = Math.Min(2.0 * (i + 1) / Segments, 2 * progress);
            var a = ThreadPoint(t0, y0, yMid, yEnd, upper);
            var b = ThreadPoint(t1, y0, yMid, yEnd, upper);
            Drawing.LineZ(a.x * sx, a.y * sy, b.x * sx, b.y * sy, color,
                Math.Max(2, (int)Math.Round(5 * Math.Min(sx, sy))));
        }
    }

    private static (double x, double y) ThreadPoint(double t, double start, double middle, double end, bool upper)
    {
        if (t <= 1)
            return Bezier((600, start), (686, upper ? 419 : 282), (upper ? 698 : 699, middle), (801, middle), t);
        return Bezier((801, middle), (904, middle), (upper ? 918 : 917, upper ? 417 : 283),
            (1000, end), t - 1);
    }

    private static (double x, double y) Bezier((double x, double y) a, (double x, double y) b,
        (double x, double y) c, (double x, double y) d, double t)
    {
        double u = 1 - t;
        return (u * u * u * a.x + 3 * u * u * t * b.x + 3 * u * t * t * c.x + t * t * t * d.x,
                u * u * u * a.y + 3 * u * u * t * b.y + 3 * u * t * t * c.y + t * t * t * d.y);
    }
}
