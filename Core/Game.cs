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

    private volatile bool _running;
    private Thread? _updateThread;
    private readonly object _gameLock = new();

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
            while (!platform.ShouldClose)
            {
                AstrumCore.InitDrop();
                Update(game);
                Draw(game);
            }
            return;
        }

        _running = true;

        // 更新スレッド開始
        _updateThread = new Thread(UpdateLoop)
        {
            IsBackground = true,
            Name = "AstrumLoom.UpdateThread"
        };
        _updateThread.Start();

        // メインスレッドは描画ループだけ
        while (!platform.ShouldClose && _running)
        {
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
    private void UpdateLoop()
    {
        try
        {
            while (!platform.ShouldClose && _running)
            {
                AstrumCore.InitDrop(); // もともと Loop() の先頭で呼んでたやつ :contentReference[oaicite:5]{index=5}
                Update(game);
            }
        }
        catch (Exception ex)
        {
            Log.EmptyLine();
            Log.Error("Update thread error: " + ex);
            Log.EmptyLine();
            Log.Write("エラーです:/ ごめんねなの！");
            platform.Close();
        }
    }

    public void Update(IGame game)
    {
        platform.UTime.BeginFrame();
        Sleep.Update();
        KeyInput.Update(platform.UTime.DeltaTime);
        Mouse.Update();
        lock (_gameLock)
            game.Update(platform.UTime.DeltaTime);
        platform.UTime.EndFrame();
        AstrumCore.UpdateFPS.Tick(platform.UTime.TotalTime);
    }
    public void Draw(IGame game)
    {
        platform.Time.BeginFrame();
        MainUpdate(game);

        platform.Graphics.BeginFrame();
        platform.Graphics.Clear(BackgroundColor);

        lock (_gameLock)
            game.Draw();
        // ★ ここでオーバーレイ
        if (showOverlay)
            Overlay.Current.Draw();
        Log.Draw();

        platform.Graphics.EndFrame();
        platform.Time.EndFrame();
        AstrumCore.DrawFPS.Tick(platform.Time.TotalTime);
    }
    public void MainUpdate(IGame game) => platform.PollEvents();
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
