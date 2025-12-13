// Sandbox/SandboxOverlay.cs
using AstrumLoom;
using AstrumLoom.Extend;

namespace Sandbox;

internal sealed class SandboxOverlay : Overlay
{
    private readonly IFont? _small;
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
    }

    public override void Draw()
    {
        if (Scene.NowScene is SimpleTestGame s)
        {
            if (s.SceneName == "LoadCheckScene")
            {
                // 簡易描画のみ
                FPS.Draw(ReferencePoint.TopRight);
                return;
            }
        }

        var platform = AstrumCore.Platform;
        // FPS / 時刻 を描く
        string fps = $"{platform.BackendKind} {AstrumCore.NowFPS}";
        string time = $"{DateTime.Now:G}";
        _small.Draw(10, 10, fps, Color.White);
        _small.Draw(10, 50, time, new Color(180, 200, 220));

        var c = gradation.GetColor((float)(Math.Sin(DateTime.Now.TimeOfDay.TotalSeconds) + 1) / 2);
        Drawing.DefaultText(10, 80, "AstrumLoom Sandbox", c);
    }
}
