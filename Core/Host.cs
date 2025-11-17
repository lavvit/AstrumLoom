namespace AstrumLoom;

public sealed class GameHost : IDisposable
{
    public GameConfig Config { get; }
    public IGamePlatform Platform { get; }
    public IGame Game { get; }

    private readonly GameRunner _runner;

    public GameHost(
        GameConfig config,
        IGamePlatform platform,
        IGame game)
    {
        Config = config;
        Platform = platform;
        Game = game;

        Platform.Time.TargetFps = config.TargetFps;
        _runner = new GameRunner(platform, game, config.ShowFpsOverlay);
    }

    public void Run() => _runner.Run();

    public void Dispose() => Platform.Dispose();
}
