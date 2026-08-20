using System.Collections.Concurrent;
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
        game.Initialize();
        AstrumCore.InitCompleted = true;
        Scene.Start();
        Sleep.WakeUp();
        Loop();
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
                AstrumCore.InitDrop();
                MainUpdate(game);
                Update(game);
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

            // メインスレッドは描画ループだけ
            while (!platform.ShouldClose && _running && !_fatalTriggered)
            {
                // 処理開始時にメインスレッドでの破棄要求を処理
                AstrumCore.ProcessPendingDisposals();
                MainUpdate(game);
                if (_mainThreadActions.TryDequeue(out var action))
                {
                    try { action(); }
                    catch (Exception ex)
                    {
                        HandleFatal(ex, "MainThreadAction");
                    }
                }

                // Drop 初期化は「Update 側で」やりたいなら UpdateLoop に移してもOK
                Draw(game);
            }

            // 終了シグナル
            _running = false;

            // 更新スレッド終了待ち
            if (_updateThread != null && _updateThread.IsAlive)
            {
                try
                {
                    _updateThread.Join();
                }
                catch { /* 終了中の例外は無視でOK */ }
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
            while (!platform.ShouldClose && _running && !_fatalTriggered)
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
        try
        {
            Sleep.Update();

            // 生入力を先に進めておく。こうするとデバッグホットキーは
            // 一時停止中でも、入力再生中でも効く。
            _inputBridge?.PreUpdate();
            DebugControl.PollHotkeys();

            // 一時停止・スローの判定はループ 1 回につき 1 度。
            bool run = DebugControl.ShouldRunUpdate();

            // 入力の確定はループ 1 回につき 1 度だけ。
            // キャッチアップで複数ステップ走る場合、それらは同じ入力を共有する。
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
        }
        catch (Exception ex)
        {
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

        _accumulator += wallDelta;

        // ブレークポイントや初回フレームで巨大な dt が来ても、一気に走らせない。
        float maxAccum = fixedDt * Math.Max(1, config.MaxCatchUpSteps);
        if (_accumulator > maxAccum) _accumulator = maxAccum;

        int steps = 0;
        while (_accumulator >= fixedDt && steps < Math.Max(1, config.MaxCatchUpSteps))
        {
            _accumulator -= fixedDt;
            steps++;
            LogicStep(game, fixedDt);
        }
    }

    /// <summary>論理フレームを 1 回進めます。入力は呼び出し側で確定済みです。</summary>
    private void LogicStep(IGame game, float deltaTime)
    {
        AstrumCore.BeginLogicFrame(deltaTime);

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
    public void Draw(IGame game)
    {
        platform.Time.BeginFrame();
        bool frameBegan = false;
        try
        {
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
                catch { }
            }
            platform.Time.EndFrame();
            AstrumCore.DrawFPS.Tick(platform.Time.TotalTime);
            AstrumCore.CountDrawFrame();
        }

        if (_fatalTriggered)
            return;
    }
    /// <summary>ウィンドウ/OSイベントのポーリングのみ行う。マルチスレッド時もメインスレッドから毎ループ呼ばれる。</summary>
    public void MainUpdate(IGame game) => platform.PollEvents();

    private static ConcurrentQueue<Action> _mainThreadActions = new();
    /// <summary>メインスレッド専用の処理を依頼する。既にメインスレッドなら即実行、そうでなければキューに積んで次のDrawループで実行される。</summary>
    internal static void RequestToMainThread(Action action)
    {
        if (Environment.CurrentManagedThreadId == AstrumCore.MainThreadId)
        {
            action();
            return;
        }
        _mainThreadActions.Enqueue(action);
    }

    private static ConcurrentQueue<(string key, Action action)> _mainThreadBeginActions = new();
    private static ConcurrentQueue<(string key, Action action)> _mainThreadEndActions = new();
    /// <summary>
    /// 描画フレームの開始前/終了後に1回だけ実行される拡張フックを登録する。
    /// 同じkeyが既にキューにあれば追加しない（1フレームに複数回積まれるのを防ぐ）。
    /// </summary>
    internal static void AddExtendAction(string key, Action action, bool inEndStart = true)
    {
        var queue = inEndStart ? _mainThreadEndActions : _mainThreadBeginActions;
        if (queue.Any(item => item.key == key))
            return;
        queue.Enqueue((key, action));
    }
    /// <summary>Draw内で開始前/終了後に登録済みの拡張フックを全て実行する。個々の例外はログに残して継続する。</summary>
    private static void ExtendAction(bool end)
    {
        var queue = end ? _mainThreadEndActions : _mainThreadBeginActions;
        while (queue.TryDequeue(out var item))
        {
            try
            {
                item.action();
            }
            catch (Exception ex)
            {
                Log.Error($"ExtendAction error ({item.key}): {ex}");
            }
        }
    }

    /// <summary>致命的エラーを記録してループを止める。複数スレッドから同時に呼ばれても最初の1件だけを採用する。</summary>
    private void HandleFatal(Exception ex, string phase)
    {
        if (_fatalTriggered)
            return;

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