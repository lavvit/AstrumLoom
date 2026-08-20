using AstrumLoom;

namespace Sandbox;

internal sealed class SimpleTestGame : Scene
{
    private Scene? _scene;

    public override void Enable()
    {
        var ft = FontHandle.Create("ＤＦ太丸ゴシック体 Pro-5", 24, edge: 2);
        if (ft != null) Drawing.DefaultFont = ft;

        _scene = new TextureDemoScene();
        _scene?.Enable();
        Overlay.Set(new SandboxOverlay());
    }

    /// <summary>今表示している子シーン。セルフテストから状態を見るために公開する。</summary>
    public Scene? Child => _scene;

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
    private static int Main(string[] args)
    {
        // ここでゲームごとの設定を書く。コマンドライン引数で上書きされる。
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
            GraphicsBackend = GraphicsBackendKind.DxLib, // --backend raylib で切替
        };

        DefineSelfTest();

        // バックエンドの生成も引数の解釈も GameApp が引き受ける。
        return GameApp.Run(args, config, () => new SimpleTestGame());
    }

    /// <summary>--selftest で走るテスト計画。</summary>
    private static void DefineSelfTest()
    {
        SelfTest.Wait(30);
        SelfTest.Check("最初のシーンが立ち上がっている",
            () => Scene.NowScene is SimpleTestGame { Child: not null });
        SelfTest.Check("論理フレームが進んでいる", () => AstrumCore.FrameCount >= 30);
        SelfTest.Shot("boot");

        SelfTest.Wait(20);
        SelfTest.Check("スクリーンショットが保存された", () => Snapshot.Saved >= 1,
            "描画スレッドが要求を処理できていない可能性があります。");

        // 入力の合成が効いているかを確かめる。
        // Press した効果は次のフレームの入力確定から反映されるので、必ず Wait を挟む。
        SelfTest.Do("Z キーを押す", () => VirtualInput.Press(Key.Z));
        SelfTest.Wait(2);
        SelfTest.Check("Z キーの合成入力が届いた", () => Key.Z.Hold());
        SelfTest.Do("Z キーを離す", () => VirtualInput.Release(Key.Z));
        SelfTest.Wait(2);
        SelfTest.Check("合成入力が解除された", () => !Key.Z.Hold());

        SelfTest.Wait(20);
        SelfTest.Shot("final");
        SelfTest.Check("致命的エラーが出ていない", () => !AstrumCore.HasFatalError);
    }
}
