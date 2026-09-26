using System.Runtime.InteropServices;
using System.Text;

namespace AstrumLoom;

/// <summary>ゲーム本体が実装するインターフェース。GameRunnerがこれのUpdate/Drawをループから呼び出す。</summary>
public interface IGame
{
    void Initialize();
    void Update(float deltaTime);
    void Draw();
}

/// <summary>
/// メインループの実体。シングルスレッド構成では Update→Draw を1ループで交互に、
/// マルチスレッド構成（UseMultiThreadUpdate）では専用の更新スレッドを立てて
/// メインスレッドは描画とプラットフォームイベントのポーリングに専念する。
/// 固定ステップ／可変ステップ／ロックステップの3つの時間進行モードもここで吸収する。
/// </summary>
public sealed class GameRunner(IGamePlatform platform, IGame game, GameConfig config)
{
    private static readonly Color BackgroundColor = new(10, 10, 11);
    private static readonly Color FatalBackgroundColor = new(12, 4, 6);
    private static readonly TimeSpan FatalDisplayDuration = TimeSpan.FromMinutes(1);
    private static DateTime? ThrowErrorTime = null;

    private volatile bool _running;
    private Thread? _updateThread;
    private readonly object _gameLock = new();
    private volatile bool _fatalTriggered;
    private bool _drawing;
    private bool _polling;
    private bool _modalRendering;
    private int _fatalClaimed;

    /// <summary>固定ステップ更新の未消化時間（秒）。</summary>
    private float _accumulator;
    private InputBridge? _inputBridge;

    /// <summary>ゲームの初期化からメインループ開始までを行う。GameHost.Runから1度だけ呼ばれる想定。</summary>
    public void Run()
    {
        AstrumCore.Platform = platform;
        AstrumCore.MainThreadId = Environment.CurrentManagedThreadId;

        // 記録・再生・合成入力を差し込めるように、プラットフォーム入力を常に包む。
        var (input, mouse) = InputCapture.Install(platform, config, DebugSession.Options);
        _inputBridge = input as InputBridge;

        KeyInput.Initialize(input, platform.TextInput);
        Mouse.Init(mouse, config.ShowMouse);
        BootTimer.Mark("入力まわりの初期化");

        // 初期化中（Initialize/Scene.Start）の例外は Loop() の try に乗らないため、
        // ここで捕まえて HandleFatal に回さないと Boot を突き抜けて素の未処理例外になる。
        try
        {
            game.Initialize();
            AstrumCore.InitCompleted = true;
            BootTimer.Mark("game.Initialize");
            Scene.Start();
            BootTimer.Mark("最初のシーンの Enable");
        }
        catch (Exception ex)
        {
            HandleFatal(ex, "Initialize");
            RenderFatalAndClose();
            return;
        }

        Sleep.WakeUp();
        using var modal = WindowsModalRenderer.Attach(platform.WindowHandle, () =>
        {
            if (_polling && !_drawing && !_modalRendering && !_fatalTriggered && !platform.ShouldClose)
            {
                _modalRendering = true;
                try
                {
                    AstrumCore.ProcessPendingDisposals();
                    PumpMainActions();
                    DrawFrame(game, throttle: false);
                }
                finally { _modalRendering = false; }
            }
        }, ex => HandleFatal(ex, "ModalDraw"));
        try { Loop(); }
        finally { AstrumCore.CancelUpdate(); }
    }

    /// <summary>
    /// メインループ本体。MultiThreading設定でシングル/マルチスレッド構成を切り替える。
    /// 致命的エラーが起きた場合は途中でループを抜け、末尾でエラー画面表示に切り替わる。
    /// </summary>
    public void Loop()
    {
        if (!AstrumCore.MultiThreading)
        {
            while (!platform.ShouldClose && !_fatalTriggered)
            {
                AstrumCore.ProcessPendingDisposals();
                AstrumCore.PumpDropFiles();
                AstrumCore.InitDrop();
                MainUpdate(game);
                Update(game);
                PumpMainActions();
                Draw(game);
            }
        }
        else
        {
            _running = true;

            // 更新スレッド開始
            _updateThread = new Thread(UpdateLoop)
            {
                IsBackground = true,
                Name = "AstrumLoom.UpdateThread"
            };
            _updateThread.Start();

            try
            {
                while (!platform.ShouldClose && _running && !_fatalTriggered)
                {
                    AstrumCore.ProcessPendingDisposals();
                    AstrumCore.PumpDropFiles();
                    MainUpdate(game);
                    PumpMainActions();
                    if (!platform.ShouldClose && !_fatalTriggered) Draw(game);
                }
            }
            catch (Exception ex) { HandleFatal(ex, "MainLoop"); }
            finally
            {
                _running = false;
                AstrumCore.CancelUpdate();
                // 更新終了前にGPU等を破棄しない。メイン依頼待ちとの相互待機も避ける。
                while (!_updateThread.Join(1))
                {
                    PumpMainActions();
                    AstrumCore.ProcessPendingDisposals();
                    try { platform.PollEvents(); AstrumCore.ServiceMainFrame(); }
                    catch (Exception ex) { HandleFatal(ex, "ShutdownPoll"); }
                }
            }
        }

        if (_fatalTriggered)
        {
            RenderFatalAndClose();
        }
    }
    /// <summary>マルチスレッド構成時、更新専用スレッドで回り続けるループ。例外は握りつぶさずHandleFatalへ回す。</summary>
    private void UpdateLoop()
    {
        try
        {
            while (_running && !_fatalTriggered)
            {
                AstrumCore.InitDrop(); // もともと Loop() の先頭で呼んでたやつ :contentReference[oaicite:5]{index=5}
                Update(game);
            }
        }
        catch (Exception ex)
        {
            HandleFatal(ex, "UpdateLoop");
        }
    }

    /// <summary>1回分の更新処理（入力の確定・ホットキー・デバッグ制御・論理フレーム進行）を行う。</summary>
    public void Update(IGame game)
    {
        platform.UTime.BeginFrame();
        AstrumCore.UpdateLoopCount++;
        try
        {
            Sleep.Update();

            // 論理フレームの来ない反復では、入力を一切進めずに帰る。
            //
            // 固定ステップ（FixedUpdate=true）だと、この反復が論理フレームを 1 つも
            // 走らせないことが普通にある。更新スレッドは論理レートを律速にせず全力で回るので、
            // その差は実測で 60Hz に対しておよそ 30000 回/秒――500 反復に 1 回しか
            // game.Update() が走らない。
            // 押下エッジは「押した瞬間」の 1 反復しか立たないため、ここで進めてしまうと
            // その 1 反復はほぼ確実に game.Update() の走らない反復に当たり、
            // ゲームからはキーが一度も押されなかったことになる。
            // （TaikoFine v10 でタイトルの Enter が効かなかったのはこれ。）
            //
            // 可変 dt とロックステップは 1 反復 1 論理フレームなので、従来どおり毎回進める。
            if (!IsLogicStepDue())
                return;

            // 生入力を先に進めておく。こうするとデバッグホットキーは
            // 一時停止中でも、入力再生中でも効く。
            AstrumCore.InputAdvanceCount++;
            _inputBridge?.PreUpdate();
            DebugControl.PollHotkeys();

            // 一時停止・スローの判定はループ 1 回につき 1 度。
            bool run = DebugControl.ShouldRunUpdate();

            // 入力の確定はループ 1 回につき 1 度だけ。
            // キャッチアップでは保持状態を共有し、押下・解放・ホイールは最初のステップだけ公開する。
            // 再生はフレーム番号で引くので、この反復で進む「最初の」論理フレームに合わせる。
            long frame = AstrumCore.FrameCount + 1;
            if (run) InputCapture.BeginFrame(frame);

            KeyInput.Update(platform.UTime.DeltaTime);
            Mouse.Update();
            Pad.Update();

            // 記録は入力を確定させた「後」。ここを BeginFrame と同じ場所でやると
            // 1 フレーム前の状態を書いてしまい、再生が 1 フレームずれる。
            if (run) InputCapture.EndFrame(frame);

            if (run) RunLogicSteps(game, platform.UTime.DeltaTime);
            // スローで間引いた分の時間は捨てる。溜めておくと、間引きが明けた瞬間に
            // キャッチアップが一気に走ってスローにならない。
            else DropDueStep();
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException canceled ||
                canceled.CancellationToken != AstrumCore.UpdateCancellation || !AstrumCore.UpdateCancellation.IsCancellationRequested)
                HandleFatal(ex, "Update");
        }
        finally
        {
            platform.UTime.EndFrame();

            AstrumCore.UpdateFPS.Tick(platform.UTime.TotalTime);
        }

        if (_fatalTriggered)
            return;
    }

    /// <summary>固定ステップの時間の積み上げ。<see cref="IsLogicStepDue"/> が進め、<see cref="RunLogicSteps"/> が消費します。</summary>
    private float FixedDt => (float)(1.0 / Math.Max(1e-6, config.FixedUpdateHz));

    /// <summary>
    /// この反復で論理フレームを走らせるかどうか。false なら入力を一切進めずに反復を捨てます。
    /// 実時間の積み上げもここで行います（1 反復につき 1 回だけ積むため）。
    /// </summary>
    private bool IsLogicStepDue()
    {
        // 可変 dt とロックステップは 1 反復 1 論理フレーム。間引く余地が無い。
        if (!config.FixedUpdate || config.LockStep) return true;

        // 一時停止中は論理フレームが来ないが、解除のホットキーを拾うために回し続ける。
        // 時間は積まない。積むと解除した瞬間にキャッチアップが走る。
        if (DebugControl.Paused) return true;

        float fixedDt = FixedDt;
        _accumulator += platform.UTime.DeltaTime;

        // ブレークポイントや初回フレームで巨大な dt が来ても、一気に走らせない。
        float maxAccum = fixedDt * Math.Max(1, config.MaxCatchUpSteps);
        if (_accumulator > maxAccum) _accumulator = maxAccum;

        return _accumulator >= fixedDt;
    }

    /// <summary>来ていた論理フレームを走らせずに捨てます（スローでの間引き）。</summary>
    private void DropDueStep()
    {
        if (!config.FixedUpdate || config.LockStep) return;

        float fixedDt = FixedDt;
        if (_accumulator >= fixedDt) _accumulator -= fixedDt;
    }

    /// <summary>
    /// この反復で進めるべき論理フレームを実行します。
    /// 可変 dt / 固定ステップ / ロックステップの 3 モードをここで吸収します。
    /// </summary>
    private void RunLogicSteps(IGame game, float wallDelta)
    {
        if (!config.FixedUpdate)
        {
            LogicStep(game, wallDelta);
            return;
        }

        float fixedDt = (float)(1.0 / Math.Max(1e-6, config.FixedUpdateHz));

        // ロックステップは実時間を見ない。1 ループ 1 ステップ。
        if (config.LockStep)
        {
            LogicStep(game, fixedDt);
            return;
        }

        // 実時間の積み上げと上限の頭打ちは IsLogicStepDue で済ませてある。
        // ここで足すと 1 反復につき 2 回積むことになる。
        int steps = 0;
        try
        {
            while (_accumulator >= fixedDt && steps < Math.Max(1, config.MaxCatchUpSteps))
            {
                InputStep.EdgesSuppressed = steps > 0;
                _accumulator -= fixedDt;
                steps++;
                LogicStep(game, fixedDt);
            }
        }
        finally { InputStep.EdgesSuppressed = false; }
    }

    /// <summary>論理フレームを 1 回進めます。入力は呼び出し側で確定済みです。</summary>
    private void LogicStep(IGame game, float deltaTime)
    {
        AstrumCore.BeginLogicFrame(deltaTime);
        KeyInput.AdvanceHoldTimes(deltaTime);

        if (AstrumCore.GameLock)
        {
            lock (_gameLock)
                game.Update(deltaTime);
        }
        else
        {
            game.Update(deltaTime);
        }

        DebugSession.OnLogicFrame(deltaTime);
    }
    /// <summary>1フレーム分の描画。BeginFrame/EndFrameで囲み、ゲーム本体・オーバーレイ・ログの順に描く。</summary>
    public void Draw(IGame game) => DrawFrame(game, throttle: true);
    private void DrawFrame(IGame game, bool throttle)
    {
        if (_drawing) return;
        _drawing = true;
        bool frameBegan = false;
        try
        {
            platform.Time.BeginFrame();
            ExtendAction(end: false);

            platform.Graphics.BeginFrame();
            frameBegan = true;
            platform.Graphics.Clear(BackgroundColor);

            if (AstrumCore.GameLock)
            {
                lock (_gameLock)
                    game.Draw();
            }
            else
            {
                game.Draw();
            }
            // ★ ここでオーバーレイ（F1 で切り替わる）
            if (DebugControl.ShowOverlay)
                Overlay.Current.Draw();
            // デバッグメニューはオーバーレイ非表示でも、開いていれば描画する。
            if (DebugMenu.IsOpen)
                DebugMenu.Draw();
            Log.Draw();

            ExtendAction(end: true);

            // スクリーンショットはオーバーレイとログまで含めた「見えている絵」を撮る。
            DebugSession.OnDrawFrame();
        }
        catch (Exception ex)
        {
            HandleFatal(ex, "Draw");
        }
        finally
        {
            if (frameBegan)
            {
                try { platform.Graphics.EndFrame(); }
                catch (Exception ex) { HandleFatal(ex, "EndFrame"); }
            }
            try
            {
                if (throttle) platform.Time.EndFrame();
                AstrumCore.DrawFPS.Tick(platform.Time.TotalTime);
                AstrumCore.CountDrawFrame();
            }
            finally { _drawing = false; }
        }

        // 最初の 1 枚が出た時点が「起動が終わった」と感じる瞬間なので、ここで締める。
        if (AstrumCore.DrawFrameCount == 1)
        {
            BootTimer.Mark("最初の描画");
            BootTimer.Report();
        }

        if (_fatalTriggered)
            return;
    }
    /// <summary>ウィンドウ/OSイベントのポーリングのみ行う。マルチスレッド時もメインスレッドから毎ループ呼ばれる。</summary>
    public void MainUpdate(IGame game)
    {
        _polling = true;
        try { platform.PollEvents(); AstrumCore.ServiceMainFrame(); }
        finally { _polling = false; }
    }

    private static readonly MainThreadQueue _mainThreadActions = new();
    private static readonly MainThreadQueue _mainThreadBeginActions = new();
    private static readonly MainThreadQueue _mainThreadEndActions = new();

    internal static int PendingMainActions => _mainThreadActions.Count;
    internal static void ResetActions()
    {
        _mainThreadActions.Clear();
        _mainThreadBeginActions.Clear();
        _mainThreadEndActions.Clear();
    }
    internal static bool TryRequestToMainThread(Action action, string? key = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Environment.CurrentManagedThreadId == AstrumCore.MainThreadId)
        {
            action();
            return true;
        }
        return _mainThreadActions.TryPost(action,
            Math.Max(1, AstrumCore.WindowConfig?.MainThreadQueueCapacity ?? 4096), key);
    }
    internal static void RequestToMainThread(Action action)
    {
        if (!TryRequestToMainThread(action))
            throw new InvalidOperationException("Main thread queue is full. Use TryRequestToMainThread or keyed latest-state requests.");
    }
    internal static void AddExtendAction(string key, Action action, bool inEndStart = true)
    {
        ArgumentNullException.ThrowIfNull(key);
        var queue = inEndStart ? _mainThreadEndActions : _mainThreadBeginActions;
        if (!queue.TryPost(action, Math.Max(1, AstrumCore.WindowConfig?.MainThreadQueueCapacity ?? 4096), key, replace: false))
            throw new InvalidOperationException("Draw hook queue is full.");
    }
    private void PumpMainActions()
    {
        Drain(_mainThreadActions, ex => HandleFatal(ex, "MainThreadAction"));
    }
    private static void ExtendAction(bool end)
    {
        Drain(end ? _mainThreadEndActions : _mainThreadBeginActions,
            ex => Log.Error($"ExtendAction error: {ex}"));
    }
    private static void Drain(MainThreadQueue queue, Action<Exception> onError)
    {
        var config = AstrumCore.WindowConfig;
        int count = Math.Min(queue.Count, Math.Max(1, config.MainThreadActionsPerFrame));
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int i = 0; i < count && queue.TryTake(out var action); i++)
        {
            try { action(); }
            catch (Exception ex) { onError(ex); break; }
            if (System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds >=
                Math.Max(0.1, config.MainThreadActionBudgetMs)) break;
        }
    }
    /// <summary>致命的エラーを記録してループを止める。複数スレッドから同時に呼ばれても最初の1件だけを採用する。</summary>
    private void HandleFatal(Exception ex, string phase)
    {
        if (Interlocked.CompareExchange(ref _fatalClaimed, 1, 0) != 0) return;
        _fatalTriggered = true;
        _running = false;
        AstrumCore.ReportFatalError(phase, ex);
    }

    /// <summary>致命的エラー発生後、一定時間エラー画面を表示する。Enterキーで即座に閉じられ、Cキーで診断情報をクリップボードにコピーできる。</summary>
    private void RenderFatalAndClose()
    {
        var info = AstrumCore.FatalError;
        if (info == null)
            return;

        var endAt = DateTime.UtcNow + FatalDisplayDuration;
        ThrowErrorTime ??= DateTime.UtcNow;
        string? copyStatus = null;
        DateTime copyStatusUntil = DateTime.MinValue;

        while (DateTime.UtcNow < endAt && !platform.ShouldClose)
        {
            platform.PollEvents();
            platform.Input.Buffer();
            platform.Input.Update();

            if (platform.Input.GetKeyDown(Key.Enter))
                break;

            if (platform.Input.GetKeyDown(Key.C))
            {
                bool ok = ClipboardUtil.TrySetText(BuildFatalReport(info));
                copyStatus = ok ? "診断コードをクリップボードにコピーしました。" : "コピーに失敗しました。";
                copyStatusUntil = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            }

            platform.Time.BeginFrame();
            bool frameBegan = false;
            try
            {
                platform.Graphics.BeginFrame();
                frameBegan = true;
                platform.Graphics.Clear(FatalBackgroundColor);
                DrawFatalMessage(info, DateTime.UtcNow < copyStatusUntil ? copyStatus : null);
            }
            finally
            {
                if (frameBegan)
                {
                    try { platform.Graphics.EndFrame(); }
                    catch { }
                }
                platform.Time.EndFrame();
            }
        }

        platform.Close();
    }

    /// <summary>クリップボードに載せる診断情報。内部のコードやパスがそのまま読めないよう、通し番号だけ平文で、詳細はBase64で難読化する。</summary>
    private static string BuildFatalReport(FatalErrorInfo info)
    {
        var raw = new StringBuilder();
        raw.AppendLine($"Phase: {info.Phase}");
        raw.AppendLine($"Type: {info.ExceptionType}");
        raw.AppendLine($"Message: {info.Message}");
        raw.AppendLine($"Timestamp: {info.Timestamp:yyyy-MM-dd HH:mm:ss}");
        raw.AppendLine("--- StackTrace ---");
        raw.AppendLine(info.StackTrace);

        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw.ToString()));

        var report = new StringBuilder();
        report.AppendLine("=== AstrumLoom Error Report ===");
        report.AppendLine($"Time: {info.Timestamp:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine("(サポート窓口にそのまま貼り付けてください / Paste this as-is when reporting the issue)");
        report.AppendLine();
        report.Append(encoded);
        return report.ToString();
    }

    /// <summary>警告テープ風の斜め黒黄ストライプを帯状に描く。ゆっくり流れて目を引く。</summary>
    private static void DrawHazardStripe(double x, double y, double width, double height)
    {
        double t = Environment.TickCount64 / 1000.0;
        Drawing.Box(x, y, width, height, Color.Black);
        double stripeW = height * 0.9;
        double spacing = stripeW * 2;
        double offset = t * 40 % spacing;
        for (double sx = x - height - offset; sx < x + width + height; sx += spacing)
        {
            Drawing.Polygon(new[]
            {
                (sx, y + height), (sx + height, y),
                (sx + height + stripeW, y), (sx + stripeW, y + height)
            }, Color.Gold);
        }
    }

    private void DrawFatalMessage(FatalErrorInfo info, string? copyStatus)
    {
        double pulse = 0.5 + 0.5 * Math.Sin(Environment.TickCount64 / 220.0);

        Drawing.Box(0, 0, AstrumCore.Width, AstrumCore.Height, Color.Black, opacity: 0.75);

        // 上下に警告テープ
        DrawHazardStripe(0, 0, AstrumCore.Width, 18);
        DrawHazardStripe(0, AstrumCore.Height - 18, AstrumCore.Width, 18);

        // 脈打つ赤枠
        int borderThickness = 3 + (int)(pulse * 3);
        Drawing.Box(20, 24, AstrumCore.Width - 40, AstrumCore.Height - 48, Color.Red, thickness: borderThickness, opacity: 0.6 + pulse * 0.4);
        Drawing.Box(40, 44, AstrumCore.Width - 80, AstrumCore.Height - 88, Color.DarkRed, opacity: 0.3);

        double x = 60;
        double y = 60;
        int fontSize = Drawing.FontSize();

        // 警告三角アイコン（！）
        double triSize = fontSize * 1.6;
        double triCx = x + triSize * 0.5;
        double triCy = y + triSize * 0.55;
        Drawing.Triangle(triCx, triCy - triSize * 0.55, triCx - triSize * 0.55, triCy + triSize * 0.45, triCx + triSize * 0.55, triCy + triSize * 0.45,
            Color.Gold, opacity: 0.5 + pulse * 0.5);
        Drawing.Text(triCx - fontSize * 0.15, triCy - fontSize * 0.4, "!", Color.Black);

        Drawing.Text(x + triSize + 16, y, "アプリケーション内でエラーが発生しました。 Fatal Error has occurred.", Color.Red);
        y += fontSize * 2 + 10;
        Drawing.Text(x, y, $"発生時刻 Time: {info.Timestamp:yyyy-MM-dd HH:mm:ss}", Color.Gray);
        y += fontSize + 6;
        Drawing.Text(x, y, $"フェーズ Phase: {info.Phase}", Color.Yellow);
        y += fontSize + 10;
        Drawing.Text(x, y, $"{info.ExceptionType}: {info.Message}", Color.Gold);
        y += fontSize * 2 + 6;

#if DEBUG
        if (info?.Details.Length > 1)
        {
            Drawing.Text(x, y, "詳細情報 / Details:", Color.Orange);
            y += fontSize + 10;
            foreach (string? line in info.Details[1..].Take(10))
            {
                Drawing.Text(x, y, line, Color.White);
                y += fontSize + 6;
            }
        }
#else
        Drawing.Text(x, y, "詳細はサポート窓口へ「エラー情報をコピー」した内容をお送りください。", Color.Orange);
        y += fontSize + 10;
#endif

        // 操作案内バー
        double barY = AstrumCore.Height - 130;
        Drawing.Box(x, barY, AstrumCore.Width - 120, fontSize + 20, Color.Black, opacity: 0.4);
        Drawing.Text(x + 16, barY + 10, "[Enter] 閉じる / Close    [C] エラー情報をコピー / Copy diagnostic code", Color.Cyan);

        if (!string.IsNullOrEmpty(copyStatus))
            Drawing.Text(x + 16, barY - fontSize - 8, copyStatus, Color.LightGreen);

        // 自動クローズまでの残り時間バー
        y = AstrumCore.Height - 70;
        double w = AstrumCore.Width * 0.25;
        var endAt = ThrowErrorTime ?? DateTime.UtcNow + FatalDisplayDuration;
        Drawing.Box(x, y, w, 16, Color.Gray, opacity: 0.3);
        double ms = (endAt - DateTime.UtcNow).TotalMilliseconds;
        double progress = Easing.Ease(-ms / FatalDisplayDuration.TotalMilliseconds, EEasing.Sine, EInOut.InOut);
        Drawing.Box(x, y, w * progress, 16, Color.DeepPink);
        Drawing.Text(x, y - fontSize - 6, $"{Math.Ceiling((FatalDisplayDuration.TotalMilliseconds + ms) / 1000)}秒後に自動的に閉じます... (Enterで今すぐ閉じる)", Color.DeepPink);
    }
}

/// <summary>Win32クリップボードへの最小限のテキスト書き込み。バックエンド(DxLib/RayLib)に依存しない。</summary>
internal static class ClipboardUtil
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    /// <summary>クリップボードへの書き込みを試みる。失敗しても例外は投げず false を返す。</summary>
    public static bool TrySetText(string text)
    {
        IntPtr hGlobal = IntPtr.Zero;
        bool opened = false;
        try
        {
            opened = OpenClipboard(IntPtr.Zero);
            if (!opened)
                return false;

            EmptyClipboard();

            int byteCount = (text.Length + 1) * sizeof(char);
            hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)byteCount);
            if (hGlobal == IntPtr.Zero)
                return false;

            IntPtr target = GlobalLock(hGlobal);
            if (target == IntPtr.Zero)
                return false;

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                Marshal.WriteInt16(target, text.Length * sizeof(char), 0);
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }

            if (SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
                return false;

            // 所有権はクリップボードに移ったので、ここでは解放しない
            hGlobal = IntPtr.Zero;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (hGlobal != IntPtr.Zero)
                GlobalFree(hGlobal);
            if (opened)
                CloseClipboard();
        }
    }
}
