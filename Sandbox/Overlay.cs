// Sandbox/SandboxOverlay.cs
using AstrumLoom;

namespace Sandbox;

internal sealed class SandboxOverlay : Overlay
{
    private readonly IFont? _small;
    private readonly IFont? _large;
    private readonly Gradation gradation = new(
    [
        (0.0f, Color.Red),
        (0.2f, Color.Orange),
        (0.4f, Color.Yellow),
        (0.6f, Color.Lime),
        (0.8f, Color.Blue),
        (1.0f, Color.Purple)
    ]);

    public SandboxOverlay()
    {
        string fontName = "ＤＦ太丸ゴシック体 Pro-5";
        string fontpath = "Assets/FOT-大江戸勘亭流 Std E.otf";
        _small = FontHandle.Create(fontName, 16);
        _large = FontHandle.Create(fontpath, 32, bold: true);
    }

    public override void Draw()
    {
        var platform = AstrumCore.Platform;
        // FPS / 時刻 を描く
        _small.Draw(10, 10, $"{platform.BackendKind} {AstrumCore.NowFPS}", Color.White);
        _small.Draw(10, 30, $"{DateTime.Now:G}", new Color(180, 200, 220));

        var c = gradation.GetColor((float)(Math.Sin(DateTime.Now.TimeOfDay.TotalSeconds) + 1) / 2);
        Drawing.DefaultText(10, 80, "AstrumLoom Sandbox", c);
    }
}
