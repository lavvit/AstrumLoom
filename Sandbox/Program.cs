using AstrumLoom;
using AstrumLoom.DXLib;

namespace Sandbox;

internal sealed class SimpleTestGame : IGame
{
    private readonly IGamePlatform _platform;

    public SimpleTestGame(IGamePlatform platform) => _platform = platform;

    public void Initialize()
    {
        // TODO: テクスチャ読み込みとか
    }

    public void Update(float deltaTime)
    {
        // TODO: 入力テストとかをそのうち入れる
    }

    public void Draw()
    {
        // TODO: DXLib 実装が入ったら描画処理を書く
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

        runner.Run(); // いまは 5秒で自動終了
    }
}
