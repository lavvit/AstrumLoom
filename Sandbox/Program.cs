using AstrumLoom;
using AstrumLoom.DXLib;
using AstrumLoom.RayLib;

namespace Sandbox;

internal sealed class SimpleTestGame : IGame
{
    private readonly IGamePlatform _platform;
    private ITexture? _tex;

    public SimpleTestGame(IGamePlatform platform)
        => _platform = platform;

    public void Initialize() =>
        // 実行ファイルからの相対パスになる
        _tex = _platform.Graphics.LoadTexture("Assets/test.png");

    public void Update(float deltaTime)
    {
        // とりあえず何もしない
    }

    public void Draw()
    {
        if (_tex is null) return;

        // 画面中央あたりに描く（適当に）
        float x = 640 - _tex.Width / 2f;
        float y = 360 - _tex.Height / 2f;

        var g = _platform.Graphics;

        g.DrawTexture(_tex, x, y);
    }
}

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // ここでゲームごとの設定を書く
        var config = new GameConfig
        {
            Title = "AstrumLoom Sandbox",
            Width = 1280,
            Height = 720,
            VSync = false,
            ShowFpsOverlay = true,
            GraphicsBackend = GraphicsBackendKind.RayLib, // ←ここ変えるだけで切替
        };

        var platform = PlatformFactory.Create(config);
        var game = new SimpleTestGame(platform);
        Overlay.Set(new SandboxOverlay());
        using var host = new GameHost(config, platform, game);

        host.Run();

    }
}
internal static class PlatformFactory
{
    public static IGamePlatform Create(GameConfig config)
        => config.GraphicsBackend switch
        {
            GraphicsBackendKind.DxLib => new DxLibPlatform(config),
            GraphicsBackendKind.RayLib => new RayLibPlatform(config),
            _ => throw new NotSupportedException()
        };
}
