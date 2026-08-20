using AstrumLoom;

namespace Sandbox;

internal sealed class SimpleTestGame : Scene
{
    private Scene? _scene;
    private int _index;

    /// <summary>数字キーで切り替えられるシーンの一覧。</summary>
    private static readonly (string Label, Func<Scene> Create)[] Menu =
    [
        ("Texture の見本帳（星図アトラス）", () => new TextureDemoScene()),
        ("Sound の見本帳（灯台の夜）", () => new SoundDemoScene()),
        ("図形の見本帳（万華鏡工房）", () => new ShapesDemoScene()),
        ("Input の見本帳（入力コックピット）", () => new InputDemoScene()),
        ("描画負荷", () => new LoadCheckScene()),
        ("ゲームの雛形", () => new GameTemplateScene()),
    ];

    public override void Enable()
    {
        // --scene で起動時のシーンを指定できる（1 始まり）。証跡を撮るときに便利。
        Select(StartScene);
        Overlay.Set(new SandboxOverlay());
    }

    /// <summary>--scene で指定された起動時のシーン番号（0 始まり）。</summary>
    public static int StartScene { get; set; }

    /// <summary>シーンの数。--scene の範囲チェックに使う。</summary>
    public static int Count => Menu.Length;

    /// <summary>今表示している子シーン。セルフテストから状態を見るために公開する。</summary>
    public Scene? Child => _scene;

    /// <summary>選択中のシーン番号（0 始まり）。</summary>
    public int Index => _index;

    /// <summary>子シーンを差し替える。呼ばれるのは更新スレッドのこともあるので、重い初期化を Enable に置かないこと。</summary>
    public void Select(int index)
    {
        if (index < 0 || index >= Menu.Length) return;
        // 前のシーンを必ず片付ける。音を鳴らしっぱなしのシーンがあるので、これを省くと鳴り続ける。
        _scene?.Disable();
        _index = index;
        _scene = Menu[index].Create();
        _scene.Enable();
        Log.Write($"Scene -> [{index + 1}] {Menu[index].Label}");
    }

    public override void Update()
    {
        if (KeyInput.Ctrl && Key.Esc.Push()) AstrumCore.End();

        if (KeyInput.Ctrl && Key.Insert.Push())
        {
            // 意図エラーを出すテスト。
            throw new Exception("意図的に出したエラーです。");
        }
        if (KeyInput.Ctrl && Key.Delete.Push())
        {
            // Log.Write で出るエラーのテスト。ログに出るだけでゲームは止まらない。
            Log.Error("意図的に出したエラーです。");
            Log.Warning("意図的に出した警告です。");
        }

        // 数字キーでシーンを切り替える。子シーン側は数字キーを使っていない。
        for (int i = 0; i < Menu.Length; i++)
        {
            if ((Key.Key_1 + i).Push() && i != _index)
            {
                Select(i);
                return; // 差し替えた直後のフレームで新シーンの Update を回さない
            }
        }

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
        DrawMenuBar();

        Mouse.Draw(20);
    }

    /// <summary>画面下端に、どの数字キーで何が出るかを常に出しておく。</summary>
    private void DrawMenuBar()
    {
        double h = 26;
        double y = AstrumCore.Height - h;
        Drawing.Box(0, y, AstrumCore.Width, h, new Color(0, 0, 0, 170));

        // 幅は等分で決める。Drawing.DefaultTextSize の実測を足していく方式だと、
        // 日本語の実描画幅と合わずに項目同士が重なってしまう。
        double slot = AstrumCore.Width / (double)Menu.Length;
        for (int i = 0; i < Menu.Length; i++)
        {
            bool now = i == _index;
            double sx = slot * i;
            if (now) Drawing.Box(sx + 2, y + 2, slot - 4, h - 4, new Color(70, 110, 180, 200));
            DemoUi.NoteFont.Draw(sx + slot / 2, y + 5, $"[{i + 1}] {Menu[i].Label}",
                now ? Color.White : new Color(170, 186, 214), ReferencePoint.TopCenter);
        }
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

        // ゲーム固有の引数。登録しておくと「不明な引数」にならず、--help にも並ぶ。
        Startup.Register("scene", true, "起動時に開くシーン番号（1〜6）");
        int scene = (int)Startup.Parse(args).Number("scene", 1);
        SimpleTestGame.StartScene = Math.Clamp(scene - 1, 0, SimpleTestGame.Count - 1);

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

        // ---- Texture の見本帳 -------------------------------------------------
        // この計画は 1 番のシーンから始まる前提。--scene と一緒に使わないこと。
        SelfTest.Check("起動直後は Texture の見本帳", () => Root?.Child is TextureDemoScene);

        // 非同期ロードなので、読めるまで少し待つ。
        SelfTest.Wait(60);
        SelfTest.Check("素材の PNG が 4 枚とも読めた",
            () => Tex?.AssetsReady == true,
            "Assets\\*.png が出力先に無い可能性があります。tools\\make-sandbox-assets.ps1 を実行してください。");
        SelfTest.Check("焼き込みテクスチャが作れた",
            () => Tex?.BakedReady == true,
            "new Texture(size, action) がメインスレッド以外から呼ばれると失敗します。");
        SelfTest.Shot("texture");

        // 表示中のコマが操作で動くことを確かめる。
        // Press の効果は次フレームの入力確定からなので、必ず Wait を挟む。
        SelfTest.Do("自動送りを止める", () => VirtualInput.Press(Key.Space));
        SelfTest.Wait(2);
        SelfTest.Do("Space を離す", () => VirtualInput.Release(Key.Space));
        SelfTest.Wait(2);
        SelfTest.Do("今のコマを覚える", () => _frameBefore = Tex?.CurrentFrame ?? -1);
        SelfTest.Do("→ を押す", () => VirtualInput.Press(Key.Right));
        SelfTest.Wait(2);
        SelfTest.Do("→ を離す", () => VirtualInput.Release(Key.Right));
        SelfTest.Wait(2);
        // 手続き的に動く値なので「0 かどうか」ではなく「操作の前後で変わったか」を見る。
        SelfTest.Check("→ で切り出すコマが 1 つ進んだ",
            () => Tex != null && Tex.CurrentFrame == (_frameBefore + 1) % 8);

        SelfTest.Do("基準点を 1 つ進める", () => VirtualInput.Press(Key.A));
        SelfTest.Wait(2);
        SelfTest.Do("A を離す", () => VirtualInput.Release(Key.A));
        SelfTest.Wait(2);
        SelfTest.Check("ReferencePoint が切り替わった", () => Tex?.AnchorIndex == 1);

        // ---- Sound の見本帳 ---------------------------------------------------
        SelfTest.Do("2 番のシーンへ", () => VirtualInput.Press(Key.Key_2));
        SelfTest.Wait(2);
        SelfTest.Do("2 を離す", () => VirtualInput.Release(Key.Key_2));
        SelfTest.Wait(2);
        SelfTest.Check("Sound の見本帳へ切り替わった", () => Root?.Child is SoundDemoScene);

        SelfTest.Wait(60);
        SelfTest.Check("音の素材が 3 つとも読めた",
            () => Snd?.SoundsReady == true,
            "Assets\\*.wav が出力先に無いか、音声デバイスが使えない可能性があります。");
        SelfTest.Check("読み込みに失敗した音が無い", () => Snd?.AnyFailed == false);
        SelfTest.Check("BGM の長さが取れている（8 秒前後）",
            () => Snd != null && Snd.BgmLength is > 7000 and < 9000);
        SelfTest.Check("BGM が実際に再生されている",
            () => Snd?.BgmPlaying == true,
            "PlayStream が毎フレーム呼ばれていないか、音声デバイスがありません。");
        SelfTest.Shot("sound");

        SelfTest.Do("再生位置を覚える", () => _progressBefore = Snd?.BgmProgress ?? -1);
        SelfTest.Wait(30);
        SelfTest.Check("再生位置が進んでいる",
            () => Snd != null && Snd.BgmProgress > _progressBefore);

        SelfTest.Do("霧笛を鳴らす", () => VirtualInput.Press(Key.Space));
        SelfTest.Wait(2);
        SelfTest.Do("Space を離す", () => VirtualInput.Release(Key.Space));
        SelfTest.Wait(4);
        SelfTest.Check("霧笛の要求が届いた", () => Snd?.HornCount >= 1);

        // ---- 図形の見本帳 -------------------------------------------------------
        SelfTest.Do("3 番のシーンへ", () => VirtualInput.Press(Key.Key_3));
        SelfTest.Wait(2);
        SelfTest.Do("3 を離す", () => VirtualInput.Release(Key.Key_3));
        SelfTest.Wait(20);
        SelfTest.Check("図形の見本帳へ切り替わった", () => Root?.Child is ShapesDemoScene);
        SelfTest.Check("デフォルトで回転している", () => Shapes?.Spinning == true);

        SelfTest.Do("Space で回転を止める", () => VirtualInput.Press(Key.Space));
        SelfTest.Wait(2);
        SelfTest.Do("Space を離す", () => VirtualInput.Release(Key.Space));
        SelfTest.Wait(2);
        SelfTest.Check("回転が止まった", () => Shapes?.Spinning == false);

        SelfTest.Do("T で枠の太さを切り替える", () => VirtualInput.Press(Key.T));
        SelfTest.Wait(2);
        SelfTest.Do("T を離す", () => VirtualInput.Release(Key.T));
        SelfTest.Wait(2);
        SelfTest.Check("Thickness が切り替わった", () => Shapes?.ThicknessIndex == 2);
        SelfTest.Shot("shapes");

        // ---- Input の見本帳 -------------------------------------------------------
        SelfTest.Do("4 番のシーンへ", () => VirtualInput.Press(Key.Key_4));
        SelfTest.Wait(2);
        SelfTest.Do("4 を離す", () => VirtualInput.Release(Key.Key_4));
        SelfTest.Wait(20);
        SelfTest.Check("Input の見本帳へ切り替わった", () => Root?.Child is InputDemoScene);

        // KeyInput.Push はテキスト入力中(Typing)は常に false を返す
        // （Core/Input.cs の Typing ゲート）。だから通常キーのログ確認は
        // テキスト入力を始める前に済ませる。
        SelfTest.Do("Z キーを押す", () => VirtualInput.Press(Key.Z));
        SelfTest.Wait(2);
        SelfTest.Do("Z キーを離す", () => VirtualInput.Release(Key.Z));
        SelfTest.Wait(2);
        SelfTest.Check("キー入力がイベントログへ流れた", () => Inp != null && Inp.LogCount > 0);

        SelfTest.Do("T でテキスト入力を開始する", () => VirtualInput.Press(Key.T));
        SelfTest.Wait(2);
        SelfTest.Do("T を離す", () => VirtualInput.Release(Key.T));
        SelfTest.Wait(2);
        SelfTest.Check("テキスト入力が始まった", () => Inp?.TextActive == true);
        SelfTest.Shot("input");

        // Enter による確定はここでは確認しない。ITextInput はバックエンドのネイティブな
        // キー状態を直接見ており（docs\KNOWN-ISSUES.md の「RayLibTextInput が buffered
        // state 経由でなく IsKeyPressed 直呼び」）、VirtualInput 経由の合成入力が
        // 確実に届く保証が無いため。
        // 後片付け: KeyInput.Typing が true のままだと、これ以降すべての Key.Push/Left が
        // 常に false になり（Typing ゲート）、次のシーン切り替えの数字キーすら効かなくなる。
        // Esc は Typing 中に Push が false を返すゲートのせいで機能しない（このカードの注釈の通り）。
        // 唯一の脱出口である KeyInput.Cancel() で閉じる。
        SelfTest.Do("テキスト入力を閉じる（後片付け）", () => KeyInput.Cancel());
        SelfTest.Wait(4);
        SelfTest.Check("Typing が終わった", () => !KeyInput.Typing);

        // ---- 後片付けまで見る -------------------------------------------------
        SelfTest.Do("1 番のシーンへ戻す", () => VirtualInput.Press(Key.Key_1));
        SelfTest.Wait(2);
        SelfTest.Do("1 を離す", () => VirtualInput.Release(Key.Key_1));
        SelfTest.Wait(20);
        SelfTest.Check("Texture の見本帳へ戻った", () => Root?.Child is TextureDemoScene);

        SelfTest.Shot("final");
        SelfTest.Check("致命的エラーが出ていない", () => !AstrumCore.HasFatalError);
    }

    private static int _frameBefore = -1;
    private static double _progressBefore = -1;

    private static SimpleTestGame? Root => Scene.NowScene as SimpleTestGame;
    private static TextureDemoScene? Tex => Root?.Child as TextureDemoScene;
    private static SoundDemoScene? Snd => Root?.Child as SoundDemoScene;
    private static ShapesDemoScene? Shapes => Root?.Child as ShapesDemoScene;
    private static InputDemoScene? Inp => Root?.Child as InputDemoScene;
}
