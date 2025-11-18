// Sandbox/SandboxOverlay.cs
using AstrumLoom;

namespace Sandbox;

internal sealed class SandboxOverlay : Overlay
{
    private readonly IFont _small;
    private readonly IFont _large;
    private readonly Gradation gradation = new(
    [
        (0.0f, Color.Red),
        (0.2f, Color.Orange),
        (0.4f, Color.Yellow),
        (0.6f, Color.Lime),
        (0.8f, Color.Blue),
        (1.0f, Color.Purple)
    ]);

    public SandboxOverlay(IGraphics g)
    {
        string fontName = "ＤＦ太丸ゴシック体 Pro-5";
        string fontpath = "Assets/FOT-大江戸勘亭流 Std E.otf";
        _small = g.CreateFont(new FontSpec(fontName, 16));
        _large = g.CreateFont(new FontSpec(fontpath, 32, Bold: true));
    }

    public override void Draw(IGamePlatform platform)
    {
        // まずはベースの FPS / 時刻 を描く
        //base.Draw(platform);

        var g = platform.Graphics;

        _small.Draw(g, 10, 10, $"{platform.BackendKind} FPS {platform.Time.CurrentFps:F1}", Color.White);
        _small.Draw(g, 10, 30, $"{DateTime.Now:G}", new Color(180, 200, 220));

        // 例: 中央に “DXLib / Raylib” タイトル
        _large.Draw(g, 640, 80, "DXLib / Raylib", Color.White, point: ReferencePoint.Center);
        Drawing.Cross(640, 80, 20, Color.Red);

        // 例: 右下にちょっとしたデバッグテキスト
        Drawing.Text(1270, 710, "Sandbox Overlay", new Color(160, 220, 160), point: ReferencePoint.BottomRight);

        //Drawing.Gradation(480, 360, 320, 50, gradation, rotate: 0, colorSpace: Gradation.ColorSpace.OKLab);

        //Drawing.Polygon([(200, 500), (300, 450), (400, 500), (450, 600), (350, 650), (250, 600)], Color.Cyan);
    }
}
