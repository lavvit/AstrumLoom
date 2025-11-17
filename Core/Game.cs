namespace AstrumLoom;

public interface IGame
{
    void Initialize();
    void Update(float deltaTime);
    void Draw();
}

public sealed class GameRunner(IGamePlatform platform, IGame game, bool showOverlay = true)
{
    public void Run()
    {
        game.Initialize();
        Drawing.Initialize(platform.Graphics);

        while (!platform.ShouldClose)
        {
            platform.Time.BeginFrame();
            platform.PollEvents();

            game.Update(platform.Time.DeltaTime);

            platform.Graphics.BeginFrame();
            platform.Graphics.Clear(Color.CornflowerBlue);
            game.Draw();
            // ★ ここでオーバーレイ
            if (showOverlay)
            {
                Overlay.Current.Draw(platform);
            }
            Log.Draw(platform);
            platform.Graphics.EndFrame();

            platform.Time.EndFrame();
        }
    }
}
