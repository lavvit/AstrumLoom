using System.Diagnostics;

using DxLibDLL;

using static DxLibDLL.DX;

namespace AstrumLoom.DXLib;

public sealed class DxLibPlatform : IGamePlatform
{
    public GraphicsBackendKind BackendKind => GraphicsBackendKind.DxLib;

    public IGraphics Graphics { get; }
    public IInput Input { get; }
    public ITime Time { get; }

    public bool ShouldClose { get; private set; }
    public DxLibPlatform(GameConfig config)
    {
        ChangeWindowMode(TRUE);                                 // ウィンドウモード
        SetGraphMode(config.Width, config.Height, 32); // 解像度
        SetBackgroundColor(0, 0, 0);                // デフォルト背景
        SetDrawScreen(DX_SCREEN_BACK);                  // 裏画面へ描画
        SetWindowText(config.Title);                   // ウィンドウタイトル
        // 必要な設定いろいろ…
        if (DxLib_Init() < 0)
            throw new Exception("DxLib_Init failed");

        Time = new SimpleTime();
        Graphics = new DxLibGraphics(); // DummyGraphics の代わり
        Input = new DxLibInput();
    }

    public void PollEvents()
    {
        if (ShouldClose) return;

        // ウィンドウの×が押されたら != 0 になるので終了
        if (ProcessMessage() != 0)
        {
            ShouldClose = true;
            return;
        }

        if (CheckHitKey(KEY_INPUT_ESCAPE) != 0)
        {
            ShouldClose = true;
        }
    }

    public void Dispose() => DxLib_End();

    // --- 以下 stub 実装たち ---

    private sealed class DxLibInput : IInput
    {
        public bool GetKey(Key key) => key switch
        {
            Key.Space => DX.CheckHitKey(DX.KEY_INPUT_SPACE) != 0,
            Key.Left => DX.CheckHitKey(DX.KEY_INPUT_LEFT) != 0,
            Key.Right => DX.CheckHitKey(DX.KEY_INPUT_RIGHT) != 0,
            Key.Up => DX.CheckHitKey(DX.KEY_INPUT_UP) != 0,
            Key.Down => DX.CheckHitKey(DX.KEY_INPUT_DOWN) != 0,
            Key.Escape => DX.CheckHitKey(DX.KEY_INPUT_ESCAPE) != 0,
            _ => false
        };

        // とりあえず GetKeyDown/Up は後でちゃんと実装
        public bool GetKeyDown(Key key) => GetKey(key);
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
