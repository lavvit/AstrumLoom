using System.Diagnostics;

using static DxLibDLL.DX;

namespace AstrumLoom.DXLib;

public sealed class DxLibPlatform : IGamePlatform
{
    public GraphicsBackendKind BackendKind => GraphicsBackendKind.DxLib;

    public IGraphics Graphics { get; }
    public IInput Input { get; }
    public ITime Time { get; }
    public TextEnter TextInput { get; }
    public IMouse Mouse { get; }

    public bool ShouldClose { get; private set; }

    public bool VSync { get; private set; }

    private readonly DxLibInput _input;
    public DxLibPlatform(GameConfig config)
    {
        SetOutApplicationLogValidFlag(0); // ログファイル無効化
        ChangeWindowMode(TRUE); // ウィンドウモード
        SetWindowStyleMode(7); // 通常のウィンドウスタイル
        SetGraphMode(config.Width, config.Height, 32); // 解像度
        SetBackgroundColor(0, 0, 0); // デフォルト背景
        SetWindowText(config.Title); // ウィンドウタイトル
        SetWindowSizeExtendRate(config.Scale); // ウィンドウ拡大率
        SetAlwaysRunFlag(1); // 非アクティブでも動かす
        SetWaitVSyncFlag(0); // VSync 無効
        VSync = config.VSync;

        SetMultiThreadFlag(1); // マルチスレッド
        SetDoubleStartValidFlag(1); // 複数起動
        SetUseDirectInputFlag(0); // DirectInputコントローラー(重いため一時無効化)

        SetUseDirect3DVersion(DX_DIRECT3D_11);   // 11 を指定
                                                 // ソフトウェアレンダにしてないか確認
        SetUseSoftwareRenderModeFlag(0);

        // 必要な設定いろいろ…
        if (DxLib_Init() < 0)
            throw new Exception("DxLib_Init failed");

        Time = new SimpleTime();
        Graphics = new DxLibGraphics(); // DummyGraphics の代わり
        _input = new DxLibInput();
        Input = _input;
        TextInput = new(new DxLibTextInput(), Time);
        Mouse = new DxLibMouse();
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
        // キー状態の更新
        _input.Update();

        WaitVSync(VSync ? 1 : 0);
    }

    public void Close() => ShouldClose = true;

    public void Dispose() => DxLib_End();

    public ITexture LoadTexture(string path) =>
        new DxLibTexture(path);
    public ISound LoadSound(string path, bool streaming) =>
        new DxLibSound(path, streaming);

    // --- 以下 stub 実装たち ---

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
