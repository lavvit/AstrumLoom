using AstrumLoom;
using AstrumLoom.DXLib;
using AstrumLoom.RayLib;

namespace Sandbox;

internal sealed class SimpleTestGame : IGame
{
    private readonly IGamePlatform _platform;
    private ITexture? _tex;
    private IFont? _font;
    private string _playerName = "lavvit";

    public SimpleTestGame(IGamePlatform platform)
    {
        _platform = platform;
    }

    public void Initialize()
    {
        // 実行ファイルからの相対パスになる
        _tex = _platform.Graphics.LoadTexture("Assets/test.png");
        _font = _platform.Graphics.CreateFont(new FontSpec("ＤＦ太丸ゴシック体 Pro-5", 44));
    }

    public void Update(float deltaTime)
    {
        if (KeyInput.Push(Key.Esc) && !_platform.TextInput.IsActive)
        {
            _platform.Close();
        }


        // F2 押したら名前入力開始
        if (KeyInput.Push(Key.F2))
        {
            KeyInput.ActivateText(ref _playerName, new()
            {
                MaxLength = 16,
                EscapeCancelable = true,
                // 位置とかフォントサイズとかもここで
            });
        }
        // 入力中の監視（SeaDrop の Enter() 相当）
        else if (KeyInput.Enter(ref _playerName))
        {
            // ここに「入力確定した瞬間」の処理を書く
            Log.Write(_playerName);
        }
    }

    public void Draw()
    {
        Drawing.Fill(Color.CornflowerBlue);
        if (_tex is null) return;

        // 画面中央あたりに描く（適当に）
        float x = 640 - _tex.Width / 2f + 160f * (float)Math.Sin(_platform.Time.TotalTime);
        float y = 360 - _tex.Height / 2f;

        var g = _platform.Graphics;

        g.DrawTexture(_tex, x, y);
        KeyInput.DrawText(x, y, _playerName, Color.White, _font);

        //KeyBoard.Draw(20, 200, 16, KeyType.JPTKL, _font);
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
