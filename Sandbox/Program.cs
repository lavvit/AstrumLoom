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
        ("aup2 再生確認", () => new AnimeDemoScene()),
        ("動画の見本帳（映写室）", () => new MovieDemoScene()),
        ("SkiaSharp 焼き込み実験", () => new SkiaDemoScene()),
        ("装飾文字コスト比較（AstrumLoom vs Skiaキャッシュ）", () => new SkiaTextCompareDemoScene()),
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
        // 10 番目は 0 キー。11 番目から先はキーが足りないので、メニューバーのクリックで選ぶ。
        for (int i = 0; i < Menu.Length && i < 10; i++)
        {
            if (MenuKey(i).Push() && i != _index)
            {
                Select(i);
                return; // 差し替えた直後のフレームで新シーンの Update を回さない
            }
        }

        int hit = MenuHitTest(Mouse.X, Mouse.Y);
        if (hit >= 0 && hit != _index && Mouse.Push(MouseButton.Left))
        {
            Select(hit);
            return;
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

    /// <summary>シーン番号に割り当てた数字キー。1〜9 のあと 10 番目は 0 キー。</summary>
    private static Key MenuKey(int index) => index == 9 ? Key.Key_0 : Key.Key_1 + index;

    /// <summary>メニューバーの並び。段数・列数・1 段の高さ・上端をまとめて返す。</summary>
    private static (int Rows, int Cols, double RowH, double Top) MenuLayout()
    {
        // 1 行に詰めすぎると日本語のラベルが潰れるので、多いときは段を増やす。
        const int MaxPerRow = 6;
        int rows = (Menu.Length + MaxPerRow - 1) / MaxPerRow;
        int cols = (Menu.Length + rows - 1) / rows;
        double rowH = 26;
        return (rows, cols, rowH, AstrumCore.Height - rowH * rows);
    }

    /// <summary>座標がメニューバーのどの項目の上かを返す。どれでもなければ -1。</summary>
    private static int MenuHitTest(double x, double y)
    {
        var (rows, cols, rowH, top) = MenuLayout();
        if (y < top || y >= top + rowH * rows) return -1;
        double slot = AstrumCore.Width / (double)cols;
        int col = (int)(x / slot);
        int row = (int)((y - top) / rowH);
        if (col < 0 || col >= cols || row < 0 || row >= rows) return -1;
        int index = row * cols + col;
        return index < Menu.Length ? index : -1;
    }

    /// <summary>画面下端に、どの数字キーで何が出るかを常に出しておく。</summary>
    private void DrawMenuBar()
    {
        var (rows, cols, rowH, top) = MenuLayout();
        Drawing.Box(0, top, AstrumCore.Width, rowH * rows, new Color(0, 0, 0, 170));

        int hover = MenuHitTest(Mouse.X, Mouse.Y);

        // 幅は等分で決める。Drawing.DefaultTextSize の実測を足していく方式だと、
        // 日本語の実描画幅と合わずに項目同士が重なってしまう。
        double slot = AstrumCore.Width / (double)cols;
        for (int i = 0; i < Menu.Length; i++)
        {
            bool now = i == _index;
            double sx = slot * (i % cols);
            double y = top + rowH * (i / cols);
            if (now) Drawing.Box(sx + 2, y + 2, slot - 4, rowH - 4, new Color(70, 110, 180, 200));
            else if (i == hover) Drawing.Box(sx + 2, y + 2, slot - 4, rowH - 4, new Color(70, 110, 180, 90));

            // 11 番目から先は数字キーが無いので、番号の代わりにクリックで選ぶ印を出す。
            string head = i < 10 ? $"[{(i + 1) % 10}]" : "[*]";
            DemoUi.NoteFont.Draw(sx + slot / 2, y + 5, $"{head} {Menu[i].Label}",
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
        Startup.Register("scene", true, $"起動時に開くシーン番号（1〜{SimpleTestGame.Count}）");
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
        SelfTest.Wait(TapFrames);
        SelfTest.Do("Space を離す", () => VirtualInput.Release(Key.Space));
        SelfTest.Wait(TapFrames);
        // ここを確かめずに進むと、Space が届かず自動送りが動いたままでも
        // 「→ でコマが 1 つ進んだ」が実時間しだいで通ったり落ちたりして原因が分からなくなる。
        SelfTest.Check("Space で自動送りが止まった", () => Tex?.AutoFrame == false,
            "Space の押下が届いていません。");
        SelfTest.Do("今のコマを覚える", () => _frameBefore = Tex?.CurrentFrame ?? -1);
        SelfTest.Do("→ を押す", () => VirtualInput.Press(Key.Right));
        SelfTest.Wait(TapFrames);
        SelfTest.Do("→ を離す", () => VirtualInput.Release(Key.Right));
        SelfTest.Wait(TapFrames);
        // 手続き的に動く値なので「0 かどうか」ではなく「操作の前後で変わったか」を見る。
        SelfTest.Check("→ で切り出すコマが 1 つ進んだ",
            () => Tex != null && Tex.CurrentFrame == (_frameBefore + 1) % 8);

        SelfTest.Do("基準点を 1 つ進める", () => VirtualInput.Press(Key.A));
        SelfTest.Wait(TapFrames);
        SelfTest.Do("A を離す", () => VirtualInput.Release(Key.A));
        SelfTest.Wait(TapFrames);
        SelfTest.Check("ReferencePoint が切り替わった", () => Tex?.AnchorIndex == 1);

        // ---- Sound の見本帳 ---------------------------------------------------
        SelfTest.Do("2 番のシーンへ", () => VirtualInput.Press(Key.Key_2));
        SelfTest.Wait(TapFrames);
        SelfTest.Do("2 を離す", () => VirtualInput.Release(Key.Key_2));
        SelfTest.Wait(TapFrames);
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
        SelfTest.Wait(TapFrames);
        SelfTest.Do("Space を離す", () => VirtualInput.Release(Key.Space));
        SelfTest.Wait(TapFrames);
        SelfTest.Check("霧笛の要求が届いた", () => Snd?.HornCount >= 1);

        // ---- 図形の見本帳 -------------------------------------------------------
        SelfTest.Do("3 番のシーンへ", () => VirtualInput.Press(Key.Key_3));
        SelfTest.Wait(TapFrames);
        SelfTest.Do("3 を離す", () => VirtualInput.Release(Key.Key_3));
        SelfTest.Wait(20);
        SelfTest.Check("図形の見本帳へ切り替わった", () => Root?.Child is ShapesDemoScene);
        SelfTest.Check("デフォルトで回転している", () => Shapes?.Spinning == true);

        SelfTest.Do("Space で回転を止める", () => VirtualInput.Press(Key.Space));
        SelfTest.Wait(TapFrames);
        SelfTest.Do("Space を離す", () => VirtualInput.Release(Key.Space));
        SelfTest.Wait(TapFrames);
        SelfTest.Check("回転が止まった", () => Shapes?.Spinning == false);

        SelfTest.Do("T で枠の太さを切り替える", () => VirtualInput.Press(Key.T));
        SelfTest.Wait(TapFrames);
        SelfTest.Do("T を離す", () => VirtualInput.Release(Key.T));
        SelfTest.Wait(TapFrames);
        SelfTest.Check("Thickness が切り替わった", () => Shapes?.ThicknessIndex == 2);
        SelfTest.Shot("shapes");

        // ---- Input の見本帳 -------------------------------------------------------
        SelfTest.Do("4 番のシーンへ", () => VirtualInput.Press(Key.Key_4));
        SelfTest.Wait(TapFrames);
        SelfTest.Do("4 を離す", () => VirtualInput.Release(Key.Key_4));
        SelfTest.Wait(20);
        SelfTest.Check("Input の見本帳へ切り替わった", () => Root?.Child is InputDemoScene);

        // KeyInput.Push はテキスト入力中(Typing)は常に false を返す
        // （Core/Input.cs の Typing ゲート）。だから通常キーのログ確認は
        // テキスト入力を始める前に済ませる。
        SelfTest.Do("Z キーを押す", () => VirtualInput.Press(Key.Z));
        SelfTest.Wait(TapFrames);
        SelfTest.Do("Z キーを離す", () => VirtualInput.Release(Key.Z));
        SelfTest.Wait(TapFrames);
        SelfTest.Check("キー入力がイベントログへ流れた", () => Inp != null && Inp.LogCount > 0);

        SelfTest.Do("T でテキスト入力を開始する", () => VirtualInput.Press(Key.T));
        SelfTest.Wait(TapFrames);
        SelfTest.Do("T を離す", () => VirtualInput.Release(Key.T));
        SelfTest.Wait(TapFrames);
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
        SelfTest.Wait(TapFrames);
        SelfTest.Check("Typing が終わった", () => !KeyInput.Typing);


        // ---- 動画の見本帳 -----------------------------------------------------
        // Raylib は ffmpeg で逐次デコードする（docs\MOVIE.md）。ffmpeg が入っていない環境や
        // 素材が無い環境でも「落ちずに IsFailed になる」ことが要件なので、読めた場合と
        // 読めなかった場合で確認内容を分ける。
        SelfTest.Do("8 番のシーンへ", () => VirtualInput.Press(Key.Key_8));
        SelfTest.Wait(2);
        SelfTest.Do("8 を離す", () => VirtualInput.Release(Key.Key_8));
        SelfTest.Wait(20);
        SelfTest.Check("動画の見本帳へ切り替わった", () => Root?.Child is MovieDemoScene);

        // ffprobe → 音声抽出 → デコーダ起動、と外部プロセスを 3 回踏む。初回起動は
        // ffmpeg.exe 自体のロードで数秒かかることがあるので、たっぷり待つ。
        SelfTest.Wait(600);
        SelfTest.Check("動画の読み込みが決着した（成功でも失敗でも固まらない）",
            () => Mov != null && (Mov.MainReady || Mov.MainFailed));
        SelfTest.Check("致命的エラーになっていない", () => !AstrumCore.HasFatalError);

        SelfTest.Check("動画のサイズが取れている（640x360）",
            () => Mov == null || !Mov.MainReady || (Mov.MainWidth == 640 && Mov.MainHeight == 360),
            "Assets\\movie_clock.mp4 が壊れている可能性があります。tools\\make-sandbox-assets.ps1 -Force で作り直してください。");
        SelfTest.Check("尺が取れている（6 秒前後）",
            () => Mov == null || !Mov.MainReady || Mov.MainLength is > 5000 and < 7000);
        SelfTest.Check("再生が始まっている",
            () => Mov == null || !Mov.MainReady || Mov.MainPlaying,
            "PlayStream/Play が Draw から呼ばれていない可能性があります。");
        SelfTest.Shot("movie");

        SelfTest.Do("再生位置を覚える", () => _movieTimeBefore = Mov?.MainTime ?? -1);
        SelfTest.Wait(60);
        SelfTest.Check("再生位置が進んでいる",
            () => Mov == null || !Mov.MainReady || Mov.MainTime > _movieTimeBefore);
        SelfTest.Check("音の無い動画でも時計が進んでいる",
            () => Mov == null || !Mov.SilentReady || Mov.SilentTime > 0);

        // シークは ffmpeg を開き直すので、反映されるまでフレームを与える。
        SelfTest.Do("半ばへシークする", () => Mov?.SeekTo(0.5));
        SelfTest.Wait(90);
        SelfTest.Check("シークで再生位置が飛んだ",
            () => Mov == null || !Mov.MainReady || Mov.MainProgress > 0.35);
        SelfTest.Shot("movie-seek");

        // 6 秒の動画を 0.5 から流し切ると、ループなら先頭側へ戻ってくる。
        SelfTest.Do("終端をまたぐまで待つ", () => _movieProgressBefore = Mov?.MainProgress ?? -1);
        SelfTest.Wait(300);
        SelfTest.Check("ループで先頭へ戻った（または再生が続いている）",
            () => Mov == null || !Mov.MainReady
                || Mov.MainProgress < _movieProgressBefore || Mov.MainPlaying);

        // ---- 入力バッファの取りこぼし ---------------------------------------------
        // 描画（Buffer）と更新（Update）が別スレッド・別の回数で回ると、
        // 1 回の Update の間に押下と解放が両方入った打鍵が丸ごと消えることがあった。
        // 合成入力（VirtualInput）は InputBridge が押下集合からエッジを作るのでこの経路を
        // 通らない＝見本帳を --mt で走らせても再現しない。だから土台を直接叩いて確かめる。
        SelfTest.Check("描画が更新より速くても押下エッジが落ちない",
            InputEdgeTests.DrawFasterThanUpdate,
            "Buffer() が押下と解放の両方を書いたあとの Update() で Push/Left が消えています。");
        SelfTest.Check("更新が描画より速くても押下が再発火しない",
            InputEdgeTests.UpdateFasterThanDraw);
        SelfTest.Check("1:1 で回る単一スレッド構成の遷移が従来どおり",
            InputEdgeTests.LockStepUnchanged,
            "ここが崩れると --replay がずれます。");
        SelfTest.Check("1 回の更新の間に 2 回叩いても 2 回とも届く",
            InputEdgeTests.DoubleTapInOneWindow);
        SelfTest.Check("溜め込んだ押下エッジに上限がある",
            InputEdgeTests.PendingIsCapped);
        SelfTest.Check("取り込みと確定を別スレッドで回しても打鍵数が合う",
            () => InputEdgeTests.ConcurrentNoLoss());

        // 押下エッジは生入力を 1 回進めるごとに 1 反復しか立たない。だから
        // 「生入力を進めた回数」が「論理フレーム数」を上回ったぶんは、game.Update() の
        // 走らない反復で消費されて誰にも見られずに捨てられている。
        // 固定ステップ + 実時間キャッチアップだと更新ループは論理レートの数百倍で回るので、
        // 反復ごとに進めていた頃はここが数百倍に開き、キー入力が丸ごと落ちていた。
        SelfTest.Check("生入力を論理フレームより多く進めていない",
            () => AstrumCore.InputAdvanceCount <= AstrumCore.FrameCount + 60,
            "論理フレームの来ない反復で入力を進めています。押下エッジがそこで捨てられます。");

        SelfTest.Check("--mt を付けたときは更新スレッドが生きている",
            () => !AstrumCore.MultiThreading || Environment.CurrentManagedThreadId != AstrumCore.MainThreadId,
            "--selftest 単体では単一スレッドに倒れるので、この項目は --mt のときだけ意味を持ちます。");

        // ---- 後片付けまで見る -------------------------------------------------
        SelfTest.Do("1 番のシーンへ戻す", () => VirtualInput.Press(Key.Key_1));
        SelfTest.Wait(TapFrames);
        SelfTest.Do("1 を離す", () => VirtualInput.Release(Key.Key_1));
        SelfTest.Wait(20);
        SelfTest.Check("Texture の見本帳へ戻った", () => Root?.Child is TextureDemoScene);

        SelfTest.Shot("final");
        SelfTest.Check("致命的エラーが出ていない", () => !AstrumCore.HasFatalError);
    }

    /// <summary>
    /// 合成入力を押してから／離してから待つフレーム数。
    ///
    /// ロックステップ（--selftest の既定）なら 1 反復 = 1 論理フレームなので 2 で足ります。
    /// ですが実時間キャッチアップ（--no-lockstep）だと 1 反復で最大
    /// <see cref="GameConfig.MaxCatchUpSteps"/> 個の論理フレームがまとめて走り、
    /// それらは同じ入力を共有します（docs\INVARIANTS.md の「1 ループの中の順番」）。
    /// つまり押してから離すまでが 1 反復に収まると、押下エッジが観測されるのは
    /// そのバーストが明けたあとです。既定の 5 ステップぶんを跨げる長さにしておきます。
    /// </summary>
    private const int TapFrames = 6;

    private static int _frameBefore = -1;
    private static double _progressBefore = -1;
    private static double _movieTimeBefore = -1;
    private static double _movieProgressBefore = -1;

    private static SimpleTestGame? Root => Scene.NowScene as SimpleTestGame;
    private static TextureDemoScene? Tex => Root?.Child as TextureDemoScene;
    private static SoundDemoScene? Snd => Root?.Child as SoundDemoScene;
    private static ShapesDemoScene? Shapes => Root?.Child as ShapesDemoScene;
    private static InputDemoScene? Inp => Root?.Child as InputDemoScene;
    private static MovieDemoScene? Mov => Root?.Child as MovieDemoScene;
}
