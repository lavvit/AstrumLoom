using AstrumLoom;
using AstrumLoom.DXLib;

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

        _platform.Graphics.DrawTexture(_tex, x, y);
    }
}

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var platform = new DxLibPlatform();
        var game = new SimpleTestGame(platform);
        var runner = new GameRunner(platform, game);

        runner.Run();
    }
}
