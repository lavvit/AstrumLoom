using System.Collections.Concurrent;
using System.Diagnostics;

namespace AstrumLoom;

public interface IGame
{
    void Initialize();
    void Update(float deltaTime);
    void Draw();
}

public sealed class GameRunner(IGamePlatform platform, IGame game, bool showOverlay = true, bool showMouse = true)
{
    private static readonly Color BackgroundColor = new(10, 10, 11);
    private static readonly Color FatalBackgroundColor = new(12, 4, 6);
    private static readonly TimeSpan FatalDisplayDuration = TimeSpan.FromSeconds(6);
    private static DateTime? ThrowErrorTime = null;

    private volatile bool _running;
    private Thread? _updateThread;
    private readonly object _gameLock = new();
    private volatile bool _fatalTriggered;

    public void Run()
    {
        AstrumCore.Platform = platform;
        AstrumCore.MainThreadId = Environment.CurrentManagedThreadId;

        KeyInput.Initialize(platform.Input, platform.TextInput);
        Mouse.Init(platform.Mouse, showMouse);
        game.Initialize();
        AstrumCore.InitCompleted = true;
        Scene.Start();
        Sleep.WakeUp();
        Loop();
    }

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

    public void Update(IGame game)
    {
        platform.UTime.BeginFrame();
        try
        {
            Sleep.Update();
            KeyInput.Update(platform.UTime.DeltaTime);
            Mouse.Update();
            Pad.Update();
            lock (_gameLock)
                game.Update(platform.UTime.DeltaTime);
        }
        catch (Exception ex)
        {
            HandleFatal(ex, "Update");
        }
        finally
        {
            platform.UTime.EndFrame();
        }

        if (_fatalTriggered)
            return;

        AstrumCore.UpdateFPS.Tick(platform.UTime.TotalTime);
    }
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

            lock (_gameLock)
                game.Draw();
            // ★ ここでオーバーレイ
            if (showOverlay)
                Overlay.Current.Draw();
            Log.Draw();

            ExtendAction(end: true);
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
        }

        if (_fatalTriggered)
            return;

        AstrumCore.DrawFPS.Tick(platform.Time.TotalTime);
    }
    public void MainUpdate(IGame game) => platform.PollEvents();

    private static ConcurrentQueue<(string key, Action action)> _mainThreadBeginActions = new();
    private static ConcurrentQueue<(string key, Action action)> _mainThreadEndActions = new();
    internal static void AddExtendAction(string key, Action action, bool inEndStart = true)
    {
        var queue = inEndStart ? _mainThreadEndActions : _mainThreadBeginActions;
        if (queue.Any(item => item.key == key))
            return;
        queue.Enqueue((key, action));
    }
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

    private void HandleFatal(Exception ex, string phase)
    {
        if (_fatalTriggered)
            return;

        _fatalTriggered = true;
        _running = false;
        AstrumCore.ReportFatalError(phase, ex);
    }

    private void RenderFatalAndClose()
    {
        var info = AstrumCore.FatalError;
        if (info == null)
            return;

        var endAt = DateTime.UtcNow + FatalDisplayDuration;
        ThrowErrorTime ??= DateTime.UtcNow;
        while (DateTime.UtcNow < endAt && !platform.ShouldClose)
        {
            platform.PollEvents();
            platform.Time.BeginFrame();
            bool frameBegan = false;
            try
            {
                platform.Graphics.BeginFrame();
                frameBegan = true;
                platform.Graphics.Clear(FatalBackgroundColor);
                DrawFatalMessage(info);
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

    private void DrawFatalMessage(FatalErrorInfo info)
    {
        Drawing.Box(0, 0, AstrumCore.Width, AstrumCore.Height, Color.Black, opacity: 0.7);
        Drawing.Box(20, 20, AstrumCore.Width - 40, AstrumCore.Height - 40, Color.Red, thickness: 4);
        Drawing.Box(40, 40, AstrumCore.Width - 80, AstrumCore.Height - 80, Color.DarkRed, opacity: 0.4);
        double x = 60;
        double y = 60;
        int fontSize = Drawing.FontSize();
        Drawing.Text(x, y, "アプリケーション内でエラーが発生しました。 Fatal Error has occurred.", Color.Red);
        y += fontSize * 2 + 10;
        Drawing.Text(x, y, $"スレッド Phase: {info.Phase}", Color.Yellow);
        y += fontSize + 10;
        Drawing.Text(x, y, $"{info.ExceptionType}: {info.Message}", Color.Gold);
        y += fontSize * 2 + 10;

        if (info?.Details.Length > 1)
        {
            Drawing.Text(x, y, "詳細情報 / Details:", Color.Orange);
            y += fontSize + 10;
            foreach (string? line in info.Details[1..].Take(10))
            {
                Drawing.Text(x, y, line, Color.White);
                y += fontSize + 10;
            }
        }

        y = AstrumCore.Height - 80;
        double w = AstrumCore.Width * 0.25;
        var endAt = ThrowErrorTime ?? DateTime.UtcNow + FatalDisplayDuration;
        Drawing.Box(x, y, w, 20, Color.Gray, opacity: 0.3);
        double ms = (endAt - DateTime.UtcNow).TotalMilliseconds;
        double progress = Easing.Ease(-ms / FatalDisplayDuration.TotalMilliseconds, EEasing.Sine, EInOut.InOut);
        Drawing.Box(x, y, w * progress, 20, Color.DeepPink);
        Drawing.Text(x, y - fontSize - 10, "自動的に閉じます...", Color.DeepPink);
    }

    private static class HiResDelay
    {
        // 目安: sub-ms の仕上げに
        public static void Delay(TimeSpan duration)
        {
            var sw = Stopwatch.StartNew();
            // まずは大雑把に（1ms残すくらいまで）寝る
            var sleepUntil = duration - TimeSpan.FromMilliseconds(1);
            if (sleepUntil > TimeSpan.Zero)
                Thread.Sleep(sleepUntil);

            // 仕上げはスピンで追い込む
            while (sw.Elapsed < duration) { /* busy wait */ }
        }
    }
}