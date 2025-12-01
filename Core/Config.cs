namespace AstrumLoom;

public sealed class GameConfig
{
    // --- Window ---
    public string Title { get; set; } = "AstrumLoom Game";
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public double Scale { get; set; } = 1.0;   // 論理解像度に対する拡大率
    public bool Resizable { get; set; } = true;
    public bool RunInBackground { get; set; } = true; // 非アクティブでも動かすか
    public bool Fullscreen { get; set; } = false;
    public bool ShowMouse { get; set; } = true;

    // --- Timing / Performance ---
    public int TargetFps { get; set; } = 60;
    public bool VSync { get; set; } = false;
    public bool UseMultiThreadUpdate { get; set; } = false;
    public int SleepDurationMs { get; set; } = 1000 * 60 * 10; // 長時間放置でスリープするまで

    // --- Resources ---
    public bool AsyncResourceLoad { get; set; } = true;
    public string ContentRoot { get; set; } = AppContext.BaseDirectory ?? ".";

    // --- System / Input ---
    public bool EnableDragDrop { get; set; } = true;

    // --- Debug / Logging ---
    public bool EnableLogging { get; set; } = true;
    public bool ShowFpsOverlay { get; set; } = false;

    // --- 使用するバックエンド ---
    public GraphicsBackendKind GraphicsBackend { get; set; } = GraphicsBackendKind.DxLib;
}
