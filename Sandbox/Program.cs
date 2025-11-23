using AstrumLoom;
using AstrumLoom.DXLib;
using AstrumLoom.RayLib;

namespace Sandbox;

internal sealed class SimpleTestGame : Scene
{
    private Counter? _timer;
    private IFont? _font;
    private IFont? _kbfont;

    // 追加: 図形テストシーン
    private Scene? _scene;

    public override void Enable()
    {
        _font = FontHandle.Create("ＤＦ太丸ゴシック体 Pro-5", 24, edge: 2);
        _kbfont = FontHandle.Create("Noto Sans JP", 6, bold: true);
        Drawing.DefaultFont = _font!;
        _timer = new Counter(0, 600, true);
        _timer.Start();

        // 画面サイズは GameConfig に合わせて想定（DxLib の SetGraphMode と一致）
        _scene = new TextureSoundDemoScene();
        _scene.Enable();
        Overlay.Set(new SandboxOverlay());
    }

    public override void Update()
    {
        _timer?.Tick();
        if (Key.Esc.Push())
        {
            AstrumCore.End();
        }

        if (Key.T.Push())
        {
            if (_timer?.Running ?? false)
                _timer?.Stop();
            else
                _timer?.Start();
        }

        // 今は特にシーンの更新ロジックは不要（描画のみおしゃれ表現）
        _scene?.Update();
    }

    public override void Draw()
    {
        Drawing.Fill(Color.CornflowerBlue);
        // 先に図形シーンを描く（背景＋デコレーション）
        _scene?.Draw();

        Drawing.Box(640 - 100, 360 - 30, 200, 60, Color.Black);
        Drawing.Box(640 - 98, 360 - 28, 196, 56, Color.White);
        Drawing.Box(640 - 98, 360 - 28, 196 * _timer?.Progress ?? 0, 56, Color.Green);

        if (_scene == null)
            Drawing.Text(640, 360, "Hello, AstrumLoom!", Color.White, ReferencePoint.Center);

        KeyBoard.Draw(800, 560, size: 8, KeyType.JPFull, _kbfont);

        Mouse.Draw(20);

        Drawing.Text(20, 400, KeyInput.PressedFrameCount(Key.J));
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
            ShowMouse = true,
            ShowFpsOverlay = true,
            TargetFps = 0, // 0 にすると無制限
            GraphicsBackend = GraphicsBackendKind.RayLib, // ←ここ変えるだけで切替
        };

        try
        {
            var platform = PlatformFactory.Create(config);
            AstrumCore.Boot(config, platform, new SimpleTestGame());
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
