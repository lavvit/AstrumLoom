namespace AstrumLoom;

/// <summary>
/// 起動オプションで指定されたデバッグ・自動化機能をまとめて面倒を見る係。
/// ゲームループから 3 箇所（論理フレーム後・描画フレーム中・終了時）に呼ばれます。
/// </summary>
public static class DebugSession
{
    /// <summary>解釈済みの起動オプション。<c>GameApp.Run</c> 経由でない場合は既定値。</summary>
    public static LaunchOptions Options { get; private set; } = new();

    /// <summary>スクショ・ログ・記録の出力先（絶対パス）。</summary>
    public static string OutputDir { get; private set; } = "";

    /// <summary>自動化フラグが付いた状態で走っているか。</summary>
    public static bool Automated { get; private set; }

    private static bool _initialized;
    /// <summary>RequestQuit に渡された理由。終了ログ・自動化モードのコンソール出力に使う。</summary>
    private static string _quitReason = "";
    /// <summary>二重に終了処理が走らないようにするフラグ。</summary>
    private static bool _quitRequested;
    /// <summary>--quit-after-sec 判定用の累積経過秒。</summary>
    private static float _elapsed;

    /// <summary>ブート時に 1 回だけ呼びます。</summary>
    internal static void Initialize(GameConfig config, LaunchOptions? options)
    {
        Options = options ?? new LaunchOptions();
        Automated = Options.AutomationMode;
        _initialized = true;
        _quitRequested = false;
        _quitReason = "";
        _elapsed = 0;

        string dir = Options.AutomationMode ? Options.OutputDir : config.DebugOutputDir;
        OutputDir = Path.IsPathRooted(dir) ? dir : Path.Combine(AstrumCore.AppPath, dir);
        Snapshot.Directory = OutputDir;

        DebugControl.Enabled = config.EnableDebugHotkeys;
        DebugControl.Mode = config.DebugHotkeyMode;
        DebugControl.Modifier = config.DebugHotkeyModifier;
        DebugControl.ShowOverlay = config.ShowFpsOverlay;
        DebugControl.Reset();

        if (Options.TuningPath != null) Tune.FilePath = Options.TuningPath;
        Tune.Poll(force: true);

        SelfTest.Enabled = Options.SelfTest;
        if (Options.SelfTest && !SelfTest.HasPlan)
        {
            // 計画が無いまま --selftest すると「何も落ちなかった＝成功」と誤読される。
            Log.Warning("--selftest が指定されましたが、テスト計画が 1 件も登録されていません。");
        }

        if (Automated)
        {
            Directory.CreateDirectory(OutputDir);
            Log.Write($"自動化モード: 出力先 {AstrumCore.FilePath(OutputDir)}");
        }
    }

    /// <summary>論理フレーム（ゲーム更新）を 1 回終えるたびに呼ばれます。更新スレッド。</summary>
    internal static void OnLogicFrame(float deltaTime)
    {
        if (!_initialized) return;

        _elapsed += deltaTime;
        Tune.Poll();

        // --shot-every は論理フレーム基準なので、判定もここ（論理フレームごと）で行う。
        // 描画スレッド側で FrameCount を見ると、固定ステップのキャッチアップで
        // 1 描画フレームに論理フレームが複数進んだとき、倍数を飛ばして撮り逃す。
        int every = Options.ShotEvery;
        if (every > 0 && AstrumCore.FrameCount % every == 0)
            Snapshot.Request("auto");

        if (SelfTest.Enabled)
        {
            SelfTest.Advance();
            if (SelfTest.Finished) RequestQuit("セルフテスト完了");
        }

        if (InputCapture.ReplayFinished)
            RequestQuit("入力の再生が最後まで終わりました");

        if (Options.QuitAfterFrames > 0 && AstrumCore.FrameCount >= Options.QuitAfterFrames)
            RequestQuit($"--quit-after {Options.QuitAfterFrames} フレームに到達");

        if (Options.QuitAfterSeconds > 0 && _elapsed >= Options.QuitAfterSeconds)
            RequestQuit($"--quit-after-sec {Options.QuitAfterSeconds} 秒に到達");
    }

    /// <summary>描画フレームの中（EndFrame の直前）から呼ばれます。描画スレッド。</summary>
    internal static void OnDrawFrame()
    {
        if (!_initialized) return;

        // 撮影の要求は論理フレーム側で出す。ここは保存するだけ。
        Snapshot.Service(AstrumCore.Graphic);
    }

    /// <summary>終了を予約します。次のループでウィンドウが閉じます。</summary>
    public static void RequestQuit(string reason)
    {
        if (_quitRequested) return;
        _quitRequested = true;
        _quitReason = reason;
        Log.Write($"自動終了: {reason}");
        AstrumCore.End();
    }

    /// <summary>ゲームループを抜けたあとに 1 回だけ呼びます。</summary>
    internal static void Shutdown()
    {
        if (!_initialized) return;
        _initialized = false;

        try
        {
            InputCapture.Finish(AstrumCore.FrameCount);

            if (SelfTest.Enabled)
                SelfTest.SaveReport(Path.Combine(OutputDir, "selftest.log"));

            if (Automated)
            {
                Log.Save(Path.Combine(OutputDir, "run.log"));
                Console.WriteLine($"終了 ({(_quitReason.Length > 0 ? _quitReason : "ウィンドウが閉じられました")}) "
                    + $"/ {AstrumCore.FrameCount} フレーム / スクショ {Snapshot.Saved} 枚");
            }

            // tuning ファイルが無ければ、参照されたキーから雛形を作っておく。
            if (Tune.LoadCount == 0) Tune.Save();
        }
        catch (Exception ex)
        {
            Log.Error($"終了処理でエラーが発生しました: {ex.Message}");
        }
        finally
        {
            InputCapture.Reset();
        }
    }
}
