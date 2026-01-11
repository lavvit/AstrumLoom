using AstrumLoom;
using AstrumLoom.DXLib;
using AstrumLoom.RayLib;

namespace Sandbox;

internal sealed class SimpleTestGame : Scene
{
    private Scene? _scene;

    public override void Enable()
    {
        var ft = FontHandle.Create("ＤＦ太丸ゴシック体 Pro-5", 24, edge: 2);
        if (ft != null) Drawing.DefaultFont = ft;

        //_scene = new InputTestScene();
        _scene?.Enable();
        Overlay.Set(new SandboxOverlay());
    }

    public override void Update()
    {
        if (Key.Esc.Push()) AstrumCore.End();

        _scene?.Update();
        if (_scene == null)
        {
            AstrumCore.Droppable();
        }
    }

    public override void Draw()
    {
        Drawing.Fill(Color.CornflowerBlue);
        _scene?.Draw();
        Drawing.Text(180, 10, _scene?.Name ?? "Hello, AstrumLoom!");

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
            SleepDurationMs = 30000,
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
