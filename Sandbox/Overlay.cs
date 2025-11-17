// Sandbox/SandboxOverlay.cs
using AstrumLoom;

namespace Sandbox;

internal sealed class SandboxOverlay : Overlay
{
    public override void Draw(IGamePlatform platform)
    {
        // まずはベースの FPS / 時刻 を描く
        base.Draw(platform);

        var g = platform.Graphics;

        // 例: 中央に “DXLib / Raylib” タイトル
        g.Text(640, 80, "DXLib / Raylib", 32,
            new DrawOptions
            {
                Color = Color.White,
                Point = ReferencePoint.TopCenter,
            });

        // 例: 右下にちょっとしたデバッグテキスト
        g.Text(1270, 710, "Sandbox Overlay", 14,
            new DrawOptions
            {
                Color = new Color(160, 220, 160),
                Point = ReferencePoint.BottomRight,
            });
    }
}
