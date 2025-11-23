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
        AstrumCore.InitDrop();

        KeyInput.Initialize(platform.Input, platform.TextInput);
        Mouse.Init(platform.Mouse, showMouse);
        game.Initialize();
        AstrumCore.InitCompleted = true;
        Scene.Start();

        while (!platform.ShouldClose)
        {
            //Sleep.Update();

            platform.PollEvents();
            Update(game);
            Draw(game);
        }
    }
    public void Update(IGame game)
    {
        platform.UTime.BeginFrame();
        KeyInput.Update(platform.Time.DeltaTime);
        game.Update(platform.Time.DeltaTime);
        platform.Mouse.Update();
        platform.UTime.EndFrame();
        AstrumCore.UpdateFPS.Tick(platform.UTime.TotalTime);
    }
    public void Draw(IGame game)
    {
        platform.Time.BeginFrame();
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
}
