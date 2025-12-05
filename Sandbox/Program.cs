using AstrumLoom;
using AstrumLoom.DXLib;
using AstrumLoom.Extend;
using AstrumLoom.RayLib;

namespace Sandbox;

internal sealed class SimpleTestGame : Scene
{
    private Counter? _timer;
    private IFont? _font;
    private IFont? _kbfont;
    private Movie? _movie;
    private bool _movieactive = false;
    private SoundExtend? _soundExtend;
    private Texture? _texture;

    // 追加: 図形テストシーン
    private Scene? _scene;
    internal string SceneName => _scene?.GetType().Name ?? "";

    public override void Enable()
    {
        _font = FontHandle.Create("ＤＦ太丸ゴシック体 Pro-5", 24, edge: 2);
        _kbfont = FontHandle.Create("Noto Sans JP", 6, bold: true);
        //_movie = new("Assets/campus労働.mp4");
        _soundExtend = new("Assets/vs.VIGVANGS.ogg");
        _texture = new Texture("Assets/font.png");
        Drawing.DefaultFont = _font!;
        _timer = new Counter(0, 2000, true);
        _timer.Start();

        // 画面サイズは GameConfig に合わせて想定（DxLib の SetGraphMode と一致）
        //_scene = new FancyShapesScene(AstrumCore.Width, AstrumCore.Height);
        //_scene = new TextureSoundDemoScene();
        _scene = new LoadCheckScene(); // ← 新しい負荷可視化シーン
        _scene?.Enable();
        Overlay.Set(new SandboxOverlay());
    }

    public override void Update()
    {
        _timer?.Tick();
        if (Key.Esc.Push())
        {
            AstrumCore.End();
        }

        // 今は特にシーンの更新ロジックは不要（描画のみおしゃれ表現）
        _scene?.Update();

        if (_scene == null)
        {
            AstrumCore.Droppable();
            if (Key.T.Push())
            {
                if (_timer?.Running ?? false)
                    _timer?.Stop();
                else
                    _timer?.Start();
            }
            if (Key.N.Push())
            {
                _movieactive = !_movieactive;
            }
            if (Key.M.Push() && _movieactive)
            {
                if (_movie?.IsPlaying ?? false)
                    _movie?.Stop();
                else
                    _movie?.Play();
            }
            _soundExtend?.PlayStream();

            if (Key.F1.Push())
            {
                KeyInput.ActivateText(ref inputText);
            }
            if (KeyInput.Enter(ref inputText))
            {
                Log.Write("Input text: " + inputText);
            }
            if (Key.G.Push())
            {
                _soundExtend?.Time = 60000;
                _timer?.Value = 60000;
            }
        }
    }

    private string inputText = "Type something...";
    public override void Draw()
    {
        _soundExtend?.Pump();
        Drawing.Fill(Color.CornflowerBlue);
        // 先に図形シーンを描く（背景＋デコレーション）
        _scene?.Draw();

        if (_scene == null)
        {
            // 映像を中央に表示
            if (_movie != null && _movieactive)
            {
                int mw = _movie.Width;
                int mh = _movie.Height;
                _movie.Scale = (1280.0 / mw, 720.0 / mh);
                _movie.Draw();
            }
            Drawing.Box(640 - 100, 360 - 30, 200, 60, Color.Black);
            Drawing.Box(640 - 98, 360 - 28, 196, 56, Color.White);
            Drawing.Box(640 - 98, 360 - 28, 196 * _timer?.Progress ?? 0, 56, Color.Green);
            Drawing.Text(640 - 98, 440, $"{_timer?.Value}\n{_soundExtend?.Time}\n{_timer?.Value - _soundExtend?.Time}", Color.White);

            Drawing.Text(640, 360, "Hello, AstrumLoom!", Color.White, ReferencePoint.Center);
            Drawing.Text(20, 400, KeyInput.PressedFrameCount(Key.J));

            Gradation gradation = new([Color.Red, Color.Yellow]);
            _font?.Draw(200, 200, "Gradation", new DecorateText.DecorateOption(gradation));
            _font?.Draw(400, 200, "Texture", new DecorateText.DecorateOption(_texture!));

            KeyInput.DrawText(20, 430, inputText, Color.Black, _font);
        }
        if (SceneName != "LoadCheckScene")
            KeyBoard.Draw(800, 560, size: 8, KeyType.JPFull, _kbfont);

        Mouse.Draw(20);
    }

    public override void Drag(string str)
    {
        base.Drag(str);
        Log.Write("Dragged file: " + str);
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
            SleepDurationMs = 60000,
            ShowFpsOverlay = true,
            TargetFps = 0, // 0 にすると無制限
            UseMultiThreadUpdate = true,
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
