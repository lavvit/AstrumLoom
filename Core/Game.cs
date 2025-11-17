namespace AstrumLoom;

public interface IGame
{
    void Initialize();
    void Update(float deltaTime);
    void Draw();
}

public sealed class GameRunner
{
    private readonly IGamePlatform _platform;
    private readonly IGame _game;

    public GameRunner(IGamePlatform platform, IGame game)
    {
        _platform = platform;
        _game = game;
    }

    public void Run()
    {
        _game.Initialize();

        while (!_platform.ShouldClose)
        {
            _platform.Time.BeginFrame();
            _platform.PollEvents();

            _game.Update(_platform.Time.DeltaTime);

            _platform.Graphics.BeginFrame();
            _platform.Graphics.Clear(Color.Black);
            _game.Draw();
            _platform.Graphics.EndFrame();

            _platform.Time.EndFrame();
        }
    }
}
