using System.Diagnostics;

using static DxLibDLL.DX;

namespace AstrumLoom.DXLib;

public sealed class DxLibPlatform : IGamePlatform
{
    public GraphicsBackendKind BackendKind => GraphicsBackendKind.DxLib;

    public IGraphics Graphics { get; }
    public IInput Input { get; }
    public ITime Time { get; }

    public bool ShouldClose { get; private set; }
    public DxLibPlatform()
    {
        ChangeWindowMode(TRUE);
        SetGraphMode(1280, 720, 32);
        // 必要な設定いろいろ…
        if (DxLib_Init() < 0)
        {
            throw new Exception("DxLib_Init failed");
        }

        Time = new SimpleTime();
        Graphics = new DxLibGraphics(); // DummyGraphics の代わり
        Input = new DxLibInput();
    }

    public void PollEvents()
    {
        // TODO: 本物では ProcessMessage / ESC判定 など
        ProcessMessage();
        if (CheckHitKey(KEY_INPUT_ESCAPE) != 0)
        {
            ShouldClose = true;
        }
    }

    public void Dispose()
    {
        // TODO: DxLib_End など
    }

    // --- 以下 stub 実装たち ---

    private sealed class DummyTexture : ITexture
    {
        public int Width => 0;
        public int Height => 0;
    }

    private sealed class DxLibGraphics : IGraphics
    {
        public ITexture LoadTexture(string path) => new DummyTexture();
        public void UnloadTexture(ITexture texture) { }

        public void BeginFrame() => ClearDrawScreen();
        public void Clear(Color color) { }
        public void DrawTexture(ITexture texture, float x, float y,
                                float sx = 1f, float sy = 1f, float r = 0f)
        { }
        public void EndFrame() => ScreenFlip();
    }

    private sealed class DxLibInput : IInput
    {
        public bool GetKey(Key key) => false;
        public bool GetKeyDown(Key key) => false;
        public bool GetKeyUp(Key key) => false;
    }

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
                if (DeltaTime > 0)
                    CurrentFps = 1f / DeltaTime;
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
