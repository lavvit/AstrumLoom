using System.Diagnostics;

using Raylib_cs;

using static Raylib_cs.Raylib;

namespace AstrumLoom.RayLib;

public sealed class RayLibPlatform : IGamePlatform
{
    public GraphicsBackendKind BackendKind => GraphicsBackendKind.RayLib;

    public IGraphics Graphics { get; }
    public IInput Input { get; }
    public ITime Time { get; }
    public TextEnter TextInput { get; }
    public IMouse Mouse { get; }

    public bool ShouldClose { get; private set; }

    public RayLibPlatform(GameConfig config)
    {
        InitWindow(config.Width, config.Height, config.Title);

        if (!config.Resizable)
        {
            SetWindowState(ConfigFlags.UndecoratedWindow); // 例：必要なら調整
        }

        // AstrumLoom 側で FPS を管理するので、Raylib 側のターゲットFPSは 0 にしておく
        SetTargetFPS(0);
        SetExitKey(0); // ESC キーで終了しないようにする

        Time = new SimpleTime { TargetFps = config.TargetFps };
        Graphics = new RayLibGraphics();
        Input = new RayLibInput();
        TextInput = new(new RayLibTextInput(), Time);
        Mouse = new RayLibMouse();
    }

    public void PollEvents()
    {
        if (ShouldClose) return;

        if (WindowShouldClose())
        {
            ShouldClose = true;
            return;
        }
    }

    public void Close() => ShouldClose = true;

    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // ウィンドウが初期化済みのときだけ閉じる
        if (IsWindowReady())
        {
            CloseWindow();
        }
    }

    // ================================
    //  時間管理（DxLib版と同じノリ）
    // ================================

    private sealed class SimpleTime : ITime
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private long _lastTicks;

        public float DeltaTime { get; private set; }
        public float TotalTime => (float)_sw.Elapsed.TotalSeconds;
        public float CurrentFps { get; private set; }
        public float TargetFps { get; set; } = 60f;

        public void BeginFrame()
        {
            long now = _sw.ElapsedTicks;
            if (_lastTicks == 0)
            {
                DeltaTime = 0f;
            }
            else
            {
                long dtTicks = now - _lastTicks;
                DeltaTime = (float)dtTicks / Stopwatch.Frequency;
                if (DeltaTime > 0f)
                {
                    CurrentFps = 1f / DeltaTime;
                }
            }
            _lastTicks = now;
        }

        public void EndFrame()
        {
            if (TargetFps <= 0) return;

            double ideal = 1.0 / TargetFps;
            double remain = ideal - DeltaTime;
            if (remain > 0)
            {
                int ms = (int)(remain * 1000.0);
                if (ms > 0) Thread.Sleep(ms);
            }
        }
    }
}
