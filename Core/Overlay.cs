// Core/Overlay.cs
namespace AstrumLoom;

/// <summary>
/// F1 で表示を切り替えるデバッグオーバーレイ。
/// 画面右上に出ます（左上は <see cref="Log"/> が使うため）。
/// ゲームごとに差し替えるときは <see cref="Set"/> に派生クラスを渡します。
/// </summary>
public class Overlay
{
    // 今有効なオーバーレイ
    public static Overlay Current { get; private set; } = new Overlay();

    // 差し替え用
    public static void Set(Overlay? overlay)
        => Current = overlay ?? new Overlay();

    /// <summary>背景の帯を描くか。</summary>
    public bool DrawBackground { get; set; } = true;

    private static readonly Color HeadColor = new(230, 240, 255);
    private static readonly Color BodyColor = new(180, 200, 220);
    private static readonly Color WarnColor = new(255, 214, 102);

    // ここがベースの描画
    public virtual void Draw()
    {
        var platform = AstrumCore.Platform;
        if (platform == null) return;

        var lines = new List<(string text, Color color)>();
        Compose(lines);
        if (lines.Count == 0) return;

        int size = Math.Max(8, Drawing.FontSize());
        int width = 0;
        foreach (var (text, _) in lines)
            width = Math.Max(width, Drawing.TextSize(text).width);

        const int pad = 8;
        const int margin = 10;
        double right = AstrumCore.Width - margin;
        double top = margin;

        if (DrawBackground)
            Drawing.Box(right - width - pad * 2, top, width + pad * 2, lines.Count * size + pad * 2,
                Color.Black, opacity: 0.45);

        for (int i = 0; i < lines.Count; i++)
        {
            var (text, color) = lines[i];
            Drawing.Text(right - pad, top + pad + i * size, text, color, point: ReferencePoint.TopRight);
        }
    }

    /// <summary>表示する行を組み立てます。派生クラスから行を足すときはこれを override します。</summary>
    protected virtual void Compose(List<(string text, Color color)> lines)
    {
        var platform = AstrumCore.Platform;
        if (platform == null) return;

        var fps = AstrumCore.DrawFPS;
        double avg = fps.GetFPS(0.3);
        double max = fps.GetMaxFPS(0.3);
        double min = fps.GetMinFPS(0.3);

        lines.Add(($"{platform.BackendKind}  {avg:0.0} FPS ({min:0}-{max:0})", HeadColor));

        if (AstrumCore.MultiThreading)
            lines.Add(($"Update {AstrumCore.UpdateFPS.GetFPS(0.3):0.0} FPS  (MT)", BodyColor));

        string step = AstrumCore.IsFixedStep
            ? $"fixed {1.0 / Math.Max(1e-6, AstrumCore.WindowConfig.FixedUpdateHz) * 1000:0.0}ms"
            : $"dt {AstrumCore.DeltaTime * 1000:0.0}ms";
        lines.Add(($"frame {AstrumCore.FrameCount}  {step}", BodyColor));

        lines.Add(($"{AstrumCore.Width}x{AstrumCore.Height}  scene {Scene.NowScene.Name}", BodyColor));

        string status = DebugControl.StatusText;
        if (status.Length > 0) lines.Add((status, WarnColor));

        if (InputCapture.Recorder != null)
            lines.Add(("● REC", WarnColor));
        if (InputCapture.Player != null)
            lines.Add(("▶ REPLAY", WarnColor));
        if (Snapshot.Saved > 0)
            lines.Add(($"shots {Snapshot.Saved}", BodyColor));
        if (Tune.LoadCount > 0)
            lines.Add(($"tuning {Tune.Count} 件 (x{Tune.LoadCount})", BodyColor));

        lines.Add(($"{DateTime.Now:HH:mm:ss}", BodyColor));
    }
}
