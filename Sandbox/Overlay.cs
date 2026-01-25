// Sandbox/SandboxOverlay.cs
using AstrumLoom;

namespace Sandbox;

internal sealed class SandboxOverlay : Overlay
{
    public SandboxOverlay() { }

    public override void Draw()
    {
        FPS.Draw();
        if (Scene.NowScene is SimpleTestGame s)
            if (s.Name == "LoadCheckScene") // 簡易描画のみ
                return;

        // FPS / 時刻 を描く
        string time = $"{AstrumCore.Platform.BackendKind}\n{DateTime.Now:G}";
        Drawing.DefaultText(10, 40, time, new Color(180, 200, 220));

        var c = gradation.GetColor((float)(Math.Sin(DateTime.Now.TimeOfDay.TotalSeconds) + 1) / 2);
        Drawing.DefaultText(10, 80, "AstrumLoom Sandbox", c);
    }
    private readonly Gradation gradation = new(
    [
        (0.0f, Color.Red),
        (0.2f, Color.Orange),
        (0.4f, Color.Yellow),
        (0.6f, Color.Lime),
        (0.8f, Color.Blue),
        (1.0f, Color.Purple)
    ]);
}
