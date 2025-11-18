// Core/Overlay.cs
namespace AstrumLoom;

public class Overlay
{
    // 今有効なオーバーレイ
    public static Overlay Current { get; private set; } = new Overlay();

    // 差し替え用
    public static void Set(Overlay? overlay)
        => Current = overlay ?? new Overlay();

    private readonly FpsCounter _fps = new();

    // ここがベースの描画
    public virtual void Draw(IGamePlatform platform)
    {
        var time = platform.Time;

        // ★ 毎フレーム、ゲーム時間で Tick
        _fps.Tick(time.TotalTime);

        var g = platform.Graphics;
        string backend = platform.BackendKind.ToString();

        double avg = _fps.GetFPS(0.3);
        double max = _fps.GetMaxFPS(0.3);
        double min = _fps.GetMinFPS(0.3);

        // 1行目：昔の ToString スタイル
        Drawing.Text(10, 10,
            $"{backend} {avg:0.0} FPS ({max:0}-{min:0})",
            new Color(230, 240, 255), point: ReferencePoint.TopLeft);

        // 2行目: 現在時刻
        Drawing.Text(10, 32, $"{DateTime.Now:G}", new Color(180, 200, 220), point: ReferencePoint.TopLeft);
    }
}
