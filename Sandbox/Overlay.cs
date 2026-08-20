// Sandbox/SandboxOverlay.cs
using AstrumLoom;

namespace Sandbox;

internal sealed class SandboxOverlay : Overlay
{
    public SandboxOverlay() { }

    public override void Draw()
    {
        FPS.Draw();
        // 子シーンが軽量表示を求めているときは、右上のパネルだけにする。
        if (Scene.NowScene is SimpleTestGame { Child.Name: "LoadCheckScene" })
        {
            base.Draw();
            return;
        }

        var c = gradation.GetColor((float)(Math.Sin(DateTime.Now.TimeOfDay.TotalSeconds) + 1) / 2);
        Drawing.DefaultText(10, 80, "AstrumLoom Sandbox", c);

        // 共通のデバッグパネル（バックエンド・FPS・フレーム数・REC/REPLAY など）
        base.Draw();
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
