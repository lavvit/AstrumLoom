using AstrumLoom;
using AstrumLoom.DXLib;
using AstrumLoom.RayLib;

namespace Sandbox;

internal sealed class SimpleTestGame : IGame
{
    private readonly IGamePlatform _platform;
    private Texture? _tex;
    private IFont? _font;
    private IFont? _kbfont;

    // 追加: 図形テストシーン
    private Scene? _scene;

    public SimpleTestGame(IGamePlatform platform) => _platform = platform;

    public void Initialize()
    {
        // 実行ファイルからの相対パスになる
        _tex = new Texture("Assets/test.png");
        _font = FontHandle.Create(new FontSpec("ＤＦ太丸ゴシック体 Pro-5", 24));
        _kbfont = FontHandle.Create(new FontSpec("Noto Sans JP", 6, true));
        Drawing.DefaultFont = _font!;

        // 画面サイズは GameConfig に合わせて想定（DxLib の SetGraphMode と一致）
        //_scene = new FancyShapesScene(1280, 720);
    }

    public void Update(float deltaTime)
    {
        if (KeyInput.Push(Key.Esc) && !KeyInput.Typing)
        {
            _platform.Close();
        }

        // 今は特にシーンの更新ロジックは不要（描画のみおしゃれ表現）
        _scene?.Update();
    }

    public void Draw()
    {
        // 先に図形シーンを描く（背景＋デコレーション）
        _scene?.Draw();
        if (_scene != null) return;

        Drawing.Fill(Color.CornflowerBlue);

        if (_tex is null) return;

        // 画面中央あたりに描く（適当に）
        float x = 640 + 160f * (float)Math.Sin(_platform.Time.TotalTime);
        float y = 240;

        _tex.Scale = 1;
        _tex.Point = ReferencePoint.Center;
        _tex.Draw(x, y);
        Drawing.Cross(x, y, size: 40, color: Color.Red, thickness: 2);

        KeyBoard.Draw(10, 540, size: 10, KeyType.JPFull, _kbfont);

        Mouse.Draw(20);
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
            ShowMouse = false,
            ShowFpsOverlay = true,
            TargetFps = 0, // 0 にすると無制限
            GraphicsBackend = GraphicsBackendKind.DxLib, // ←ここ変えるだけで切替
        };

        try
        {
            var platform = PlatformFactory.Create(config);
            var game = new SimpleTestGame(platform);
            Overlay.Set(new SandboxOverlay(platform.Graphics));

            using var host = new GameHost(config, platform, game);
            host.Run();
        }
        catch (Exception ex)
        {
            // 実行時の例外をコンソールに出力して原因を特定しやすくする
            Console.Error.WriteLine("Unhandled exception:");
            Console.Error.WriteLine(ex.ToString());
            Console.Error.WriteLine("Press Enter to exit...");
            try { Console.ReadLine(); } catch { }
            throw;
        }
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
