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

    private (float t, float draw, float update) _fpsHistory = (0, 0, 0);

    public override void Draw()
    {
        var platform = AstrumCore.Platform;
        if (platform.Time.TotalTime - _fpsHistory.t >= 0.16666f)
        {
            _fpsHistory = (platform.Time.TotalTime,
                (float)AstrumCore.FPS,
                (float)AstrumCore.UpdateFPS.GetFPS());
        }

        if (Scene.NowScene is SimpleTestGame s)
        {
            if (s.SceneName == "LoadCheckScene")
            {
                // 簡易描画のみ
                int size = 10;
                var color = Sleep.Sleeping ? Color.Violet : AstrumCore.VSync ? Color.Cyan : Color.Lime;
                ShapeText.Draw(10, 10, $"D:{_fpsHistory.draw:0}\nU:{_fpsHistory.update:0}",
                    size: size, color: color, thickness: 2);
                return;
            }
        }

        // FPS / 時刻 を描く
        string fps = $"{platform.BackendKind} {AstrumCore.NowFPS}";
        string time = $"{DateTime.Now:G}";
        _small.Draw(10, 10, fps, Color.White);
        _small.Draw(10, 50, time, new Color(180, 200, 220));

        var c = gradation.GetColor((float)(Math.Sin(DateTime.Now.TimeOfDay.TotalSeconds) + 1) / 2);
        Drawing.DefaultText(10, 80, "AstrumLoom Sandbox", c);
    }
}
