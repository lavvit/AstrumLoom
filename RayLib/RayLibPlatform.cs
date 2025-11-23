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
    public ITime UTime { get; }
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
        if (!IsAudioDeviceReady())
        {
            InitAudioDevice();
        }

        Time = new SimpleTime { TargetFps = config.TargetFps };
        UTime = new SimpleTime { TargetFps = 0 };
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
    public bool IsActive => IsWindowFocused();

    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // ウィンドウが初期化済みのときだけ閉じる
        if (_ready)
        {
            CloseWindow();
        }
    }

    private bool _ready => IsWindowReady();
    public ITexture LoadTexture(string path) =>
        new RayLibTexture(path);
    public ISound LoadSound(string path, bool streaming = false) =>
        new RayLibSound(path, streaming);
    public IMovie LoadMovie(string path) =>
        new RayLibMovie(path);

    public ITexture CreateTexture(int width, int height, Action callback)
    {
        if (Environment.CurrentManagedThreadId != AstrumCore.MainThreadId)
        {
            Log.Warning("CreateTexture はメインスレッドで呼び出してください。");
            return new RayLibTexture("");
        }
        if (width <= 0 || height <= 0) return new RayLibTexture("");
        var renderTex = Raylib.LoadRenderTexture(width, height);
        Raylib.BeginTextureMode(renderTex);
        callback?.Invoke();
        Raylib.EndTextureMode();
        return new RayLibTexture(renderTex.Texture);
    }

    private bool VSync;
    public void SetVSync(bool enabled)
    {
        if (!_ready || VSync == enabled) return;
        Log.Debug("VSync切替: " + enabled);
        VSync = enabled;
        // 途中切替は SetWindowState / ClearWindowState を使う。
        if (enabled)
        {
            SetWindowState(ConfigFlags.VSyncHint); // スワップ間引き（プラットフォーム依存）
            SetTargetFPS(120); // 120 に張り付けたい場合は併用
        }
        else
        {
            ClearWindowState(ConfigFlags.VSyncHint);
            SetTargetFPS(0); // 0 で上限無し
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
