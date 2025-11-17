// Core/Overlay.cs
namespace AstrumLoom;

public class Overlay
{
    // 今有効なオーバーレイ
    public static Overlay Current { get; private set; } = new Overlay();

    // 差し替え用
    public static void Set(Overlay? overlay)
        => Current = overlay ?? new Overlay();

    // ここがベースの描画
    public virtual void Draw(IGamePlatform platform)
    {
        // とりあえず FPS + バックエンド名だけ出す簡易版
        var g = platform.Graphics;
        var fps = platform.Time.CurrentFps;          // ITime から FPS 取れる :contentReference[oaicite:1]{index=1}
        var backend = platform.BackendKind.ToString(); // DxLib / RayLib … :contentReference[oaicite:2]{index=2}

        // 1行目: バックエンド + FPS
        g.Text(10, 10, $"{backend} {fps:F1} FPS", 18,
            new DrawOptions
            {
                Color = new Color(230, 240, 255),
                Point = ReferencePoint.TopLeft,
            });

        // 2行目: 現在時刻
        g.Text(10, 32, $"{DateTime.Now:G}", 14,
            new DrawOptions
            {
                Color = new Color(180, 200, 220),
                Point = ReferencePoint.TopLeft,
            });
    }
}
