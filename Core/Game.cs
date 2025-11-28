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

    public void Run()
    {
        AstrumCore.Platform = platform;

        KeyInput.Initialize(platform.Input, platform.TextInput);
        Mouse.Init(platform.Mouse, showMouse);
        game.Initialize();
        AstrumCore.InitCompleted = true;
        Scene.Start();
        Sleep.WakeUp();
        //LoopMultiThread();
        Loop();
    }

    public void Loop()
    {
        while (!platform.ShouldClose)
        {
            AstrumCore.InitDrop();
            Update(game);
            Draw(game);
        }
    }
    public void LoopMultiThread()
    {
    }

    public void Update(IGame game)
    {
        platform.UTime.BeginFrame();
        Sleep.Update();
        KeyInput.Update(platform.UTime.DeltaTime);
        Mouse.Update();
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
