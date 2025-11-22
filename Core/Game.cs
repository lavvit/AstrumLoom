namespace AstrumLoom;

public interface IGame
{
    void Initialize();
    void Update(float deltaTime);
    void Draw();
}

public sealed class GameRunner(IGamePlatform platform, IGame game, bool showOverlay = true, bool showMouse = true)
{
    public static int MainThreadId { get; } = Environment.CurrentManagedThreadId;
    public static IGamePlatform Platform { get; private set; } = null!;
    private static readonly Color BackgroundColor = new(10, 10, 11);
    public static int Width => (int)Platform.Graphics.Size.Width;
    public static int Height => (int)Platform.Graphics.Size.Height;

    public void Run()
    {
        Platform = platform;
        Drawing.Initialize(platform.Graphics);
        KeyInput.Initialize(platform.Input, platform.TextInput);
        Mouse.Init(platform.Mouse, showMouse);
        game.Initialize();

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
            {
                Overlay.Current.Draw(platform);
            }
            Log.Draw();
            platform.Graphics.EndFrame();

            platform.Time.EndFrame();
        }
    }
}
