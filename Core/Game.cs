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
        AstrumCore.InitDrop();

        KeyInput.Initialize(platform.Input, platform.TextInput);
        Mouse.Init(platform.Mouse, showMouse);
        game.Initialize();
        AstrumCore.InitCompleted = true;
        Scene.Start();

        while (!platform.ShouldClose)
        {
            platform.Time.BeginFrame();
            platform.PollEvents();

            game.Update(platform.Time.DeltaTime);
            platform.Mouse.Update();

            platform.Graphics.BeginFrame();
            platform.Graphics.Clear(BackgroundColor);
            game.Draw();
            // ★ ここでオーバーレイ
            if (showOverlay)
                Overlay.Current.Draw();
            Log.Draw();
            platform.Graphics.EndFrame();

            platform.Time.EndFrame();
        }
    }
}
