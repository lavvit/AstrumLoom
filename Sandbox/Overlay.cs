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
        // FPS / 時刻 を描く
        _small.Draw(10, 10, $"{platform.BackendKind} FPS {platform.Time.CurrentFps:F1}", Color.White);
        _small.Draw(10, 30, $"{DateTime.Now:G}", new Color(180, 200, 220));
    }
}
